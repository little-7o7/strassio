using System.Security.Cryptography;
using System.Text;
using Strassio.Licensing;

namespace Strassio.Core.Tests.Licensing;

/// <summary>
/// Попытки обойти лицензию так, как это сделал бы пользователь без программиста: перевести часы,
/// вернуть старую копию файла, удалить или поправить метки, подсунуть свой сервер. Каждая должна
/// закончиться «работать нельзя». И обратное: честный пользователь с ошибкой в часах не застревает.
/// </summary>
public class LicenseAttackTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);

    private static readonly HardwareCode Pc = HardwareCode.FromComponents(new[] { "uuid-1", "board-1", "cpu-1", "disk-1" });

    private readonly string dir = Path.Combine(Path.GetTempPath(), "strassio-attack-" + Guid.NewGuid().ToString("N"));
    private readonly ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly LicensePublicKey key;
    private DateTime now = Now;

    /// <summary>Что ответит «сервер» на следующий запрос.</summary>
    private Func<string, ApiReply> reply = _ => ApiReply.Offline();

    public LicenseAttackTests()
    {
        ECParameters p = signer.ExportParameters(false);
        key = LicensePublicKey.FromBase64(Convert.ToBase64String(p.Q.X!.Concat(p.Q.Y!).ToArray()));
    }

    public void Dispose()
    {
        signer.Dispose();
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, true);
        }
    }

    private string LicenseFile => Path.Combine(dir, LicenseManager.FileName);

    private string MarksFile => Path.Combine(dir, "license.state");

    /// <summary>Новый запуск CorelDRAW: всё читается из файлов заново.</summary>
    private LicenseManager Start(HardwareCode? pc = null) => new(new Api(this), () => pc ?? Pc, key, dir, () => now);

    /// <summary>Лицензия, какую выдаёт сервер в момент <paramref name="issued"/> (по умолчанию — сейчас).</summary>
    private SignedDocument License(string plan = "full", DateTime? expires = null, DateTime? issued = null, bool offline = false)
    {
        DateTime at = issued ?? now;
        string exp = expires.HasValue ? $"\"expiresAt\":\"{LicenseData.ToIso(expires.Value)}\"," : string.Empty;
        string payload = "{\"serial\":\"STRS-AAAA-BBBB-CCCC\",\"hwid\":\"" + Pc.ToStorage() + "\",\"plan\":\"" + plan + "\"," + exp +
                         "\"issuedAt\":\"" + LicenseData.ToIso(at) + "\",\"nextCheckAt\":\"" + LicenseData.ToIso(at.AddDays(14)) + "\"" +
                         (offline ? ",\"offline\":true" : string.Empty) + "}";
        return new SignedDocument
        {
            Payload = payload,
            Signature = Convert.ToBase64String(signer.SignData(Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)),
        };
    }

    private void ServerGivesFreshLicense(string plan = "full", DateTime? expires = null) =>
        reply = _ => new ApiReply { Ok = true, License = License(plan, expires) };

    private async Task<LicenseManager> Activated()
    {
        ServerGivesFreshLicense();
        LicenseManager m = Start();
        Assert.True((await m.ActivateAsync("STRS-AAAA-BBBB-CCCC")).Ok);
        return m;
    }

    [Fact]
    public async Task TrialEnded_ClockSetBack_DoesNotWork()
    {
        ServerGivesFreshLicense("trial", Now.AddDays(14));
        LicenseManager m = Start();
        await m.StartTrialAsync();

        now = Now.AddDays(20);
        Assert.Equal(LicenseState.Expired, Start().Status.State);

        now = Now.AddDays(5); // «вернуться» во время пробного периода
        LicenseStatus s = Start().Status;
        Assert.Equal(LicenseState.ClockTampered, s.State);
        Assert.False(s.CanCreate);
    }

    [Fact]
    public async Task InternetOff_ClockSetBack_DoesNotExtend14Days()
    {
        await Activated();
        now = Now.AddDays(13);
        Assert.Equal(LicenseState.Valid, Start().Status.State);

        now = Now.AddDays(3);
        Assert.Equal(LicenseState.ClockTampered, Start().Status.State);
    }

    [Fact]
    public async Task ClockBeforeLicenseWasIssued_DoesNotWork()
    {
        await Activated();
        File.Delete(MarksFile);
        now = Now.AddDays(-1);
        Assert.Equal(LicenseState.ClockTampered, Start().Status.State);
    }

    [Fact]
    public async Task MarksDeleted_ClockSetBack_DoesNotWork()
    {
        ServerGivesFreshLicense("trial", Now.AddDays(14));
        await Start().StartTrialAsync();
        now = Now.AddDays(40);
        _ = Start().Status;

        File.Delete(MarksFile);
        now = Now.AddDays(5);
        Assert.Equal(LicenseState.ClockTampered, Start().Status.State);
        Assert.Equal(LicenseState.ClockTampered, Start().Status.State); // и при следующем запуске тоже
    }

    [Fact]
    public async Task MarksEdited_OrCopiedFromOtherPc_AreIgnored()
    {
        await Activated();
        string marks = File.ReadAllText(MarksFile);

        // Поправить число руками (подпись HMAC перестаёт сходиться).
        string[] parts = marks.Split('|');
        parts[1] = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks.ToString();
        File.WriteAllText(MarksFile, string.Join("|", parts));
        Assert.Equal(LicenseState.ClockTampered, Start().Status.State);

        // Метка с другого компьютера (другой ключ HMAC).
        var other = HardwareCode.FromComponents(new[] { "uuid-9", "board-9", "cpu-9", "disk-9" });
        var foreign = new TimeGuard(new IMarkStore[] { new FileMarkStore(MarksFile) }, () => other.ToStorage());
        foreign.AcceptServerTime(Now);
        Assert.Equal(LicenseState.ClockTampered, Start().Status.State);
    }

    [Fact]
    public async Task MarksMadeReadOnly_DoesNotWork()
    {
        await Activated();
        File.SetAttributes(MarksFile, FileAttributes.ReadOnly);
        try
        {
            now = Now.AddDays(1);
            Assert.Equal(LicenseState.ClockTampered, Start().Status.State);
        }
        finally
        {
            File.SetAttributes(MarksFile, FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task BackupRestored_AfterReleasingComputer_DoesNotWork()
    {
        LicenseManager m = await Activated();
        byte[] backup = File.ReadAllBytes(LicenseFile);

        now = Now.AddMinutes(5);
        reply = _ => new ApiReply { Ok = true };
        Assert.True((await m.DeactivateAsync()).Ok);

        File.WriteAllBytes(LicenseFile, backup);
        Assert.Equal(LicenseState.None, Start().Status.State);
        Assert.Equal("old_file", Start().Import(File.ReadAllText(LicenseFile)).Error);
    }

    [Fact]
    public async Task BackupRestored_AfterTransferToOtherPc_DoesNotWork()
    {
        LicenseManager m = await Activated();
        byte[] backup = File.ReadAllBytes(LicenseFile);

        now = Now.AddDays(1);
        reply = _ => new ApiReply { Ok = false, Error = "revoked" };
        Assert.True(await m.CheckAsync(force: true));

        File.WriteAllBytes(LicenseFile, backup);
        Assert.False(Start().CanCreate);
    }

    [Fact]
    public async Task BackupRestored_AndMarksDeleted_NeedsServer()
    {
        LicenseManager m = await Activated();
        byte[] backup = File.ReadAllBytes(LicenseFile);
        now = Now.AddMinutes(5);
        reply = _ => new ApiReply { Ok = true };
        await m.DeactivateAsync();

        File.WriteAllBytes(LicenseFile, backup);
        File.Delete(MarksFile);
        LicenseStatus s = Start().Status;
        Assert.Equal(LicenseState.ClockTampered, s.State);
        Assert.False(s.CanCreate);

        // Сверка с сервером: ключ на этом компьютере уже не активирован — лицензия снимается.
        reply = _ => new ApiReply { Ok = false, Error = "not_activated" };
        LicenseManager again = Start();
        Assert.True(await again.CheckAsync());
        Assert.Equal(LicenseState.None, again.Status.State);
    }

    [Fact]
    public async Task FakeServer_ReplayingOldLicense_IsRejected()
    {
        await Activated();
        SignedDocument old = License(issued: Now);

        now = Now.AddDays(20);
        ServerGivesFreshLicense();
        LicenseManager m = Start();
        Assert.True(await m.CheckAsync());

        // Подставной сервер повторяет старый настоящий ответ, чтобы опустить метку времени.
        reply = _ => new ApiReply { Ok = true, License = old };
        Assert.Equal("bad_license", (await m.ActivateAsync("STRS-AAAA-BBBB-CCCC")).Error);

        now = Now.AddDays(1);
        Assert.Equal(LicenseState.ClockTampered, Start().Status.State);
    }

    [Fact]
    public void OfflineFile_WithClockSetBack_DoesNotWork()
    {
        LicenseManager m = Start();
        Assert.True(m.Import(Json(License(expires: Now.AddDays(30), offline: true))).Ok);
        now = Now.AddDays(60);
        Assert.Equal(LicenseState.Expired, Start().Status.State);

        now = Now.AddDays(10);
        Assert.Equal(LicenseState.ClockTampered, Start().Status.State);
        Assert.Equal("clock", Start().Import(Json(License(expires: Now.AddDays(30), issued: Now, offline: true))).Error);
    }

    [Fact]
    public async Task HonestUser_ClockAccidentallyAhead_RecoversAfterCheck()
    {
        await Activated();
        now = Now.AddDays(365); // случайно поставили год вперёд
        _ = Start().Status;

        now = Now.AddDays(2); // вернули правильное время
        LicenseManager m = Start();
        Assert.Equal(LicenseState.ClockTampered, m.Status.State);

        ServerGivesFreshLicense();
        await m.CheckAsync();
        Assert.Equal(LicenseState.Valid, m.Status.State);
        Assert.Equal(LicenseState.Valid, Start().Status.State);
    }

    [Fact]
    public async Task LocalClockBehindServer_IsClockTampered_UntilFixed()
    {
        LicenseManager m = Start();
        now = Now.AddDays(-10); // часы компьютера отстают на 10 дней
        reply = _ => new ApiReply { Ok = true, License = License(issued: Now) };
        Assert.Equal("clock", (await m.ActivateAsync("STRS-AAAA-BBBB-CCCC")).Error);
        Assert.False(m.CanCreate);

        now = Now.AddHours(1);
        Assert.True(Start().CanCreate);
    }

    private static string Json(SignedDocument doc) =>
        "{\"payload\":" + System.Text.Json.JsonSerializer.Serialize(doc.Payload) + ",\"signature\":\"" + doc.Signature + "\"}";

    private sealed class Api : ILicenseApi
    {
        private readonly LicenseAttackTests owner;

        public Api(LicenseAttackTests owner)
        {
            this.owner = owner;
        }

        public Task<ApiReply> PostAsync(string action, ApiRequest request, CancellationToken cancel = default) => Task.FromResult(owner.reply(action));

        public Task<ApiReply> LatestUpdateAsync(string channel, CancellationToken cancel = default) => Task.FromResult(ApiReply.Offline());
    }
}
