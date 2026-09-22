using System.Security.Cryptography;
using System.Text;
using Strassio.Licensing;

namespace Strassio.Core.Tests.Licensing;

/// <summary>Лицензия в плагине (SPEC 13.2–13.6): ввод ключа, перенос, пробный период, сверка, офлайн-файл.</summary>
public class LicenseManagerTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);

    private static readonly HardwareCode Pc = HardwareCode.FromComponents(new[] { "uuid-1", "board-1", "cpu-1", "disk-1" });

    private static readonly HardwareCode OtherPc = HardwareCode.FromComponents(new[] { "uuid-9", "board-9", "cpu-9", "disk-9" });

    private readonly string dir = Path.Combine(Path.GetTempPath(), "strassio-lic-" + Guid.NewGuid().ToString("N"));
    private readonly ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly LicensePublicKey key;
    private readonly FakeApi api = new();
    private DateTime now = Now;

    public LicenseManagerTests()
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

    private LicenseManager NewManager(HardwareCode? pc = null) => new(api, () => pc ?? Pc, key, dir, () => now);

    private SignedDocument License(HardwareCode hw, string plan = "full", DateTime? expires = null, string serial = "STRS-AAAA-BBBB-CCCC")
    {
        string exp = expires.HasValue ? $"\"expiresAt\":\"{LicenseData.ToIso(expires.Value)}\"," : string.Empty;
        string payload = "{\"serial\":\"" + serial + "\",\"hwid\":\"" + hw.ToStorage() + "\",\"plan\":\"" + plan + "\"," + exp +
                         "\"issuedAt\":\"" + LicenseData.ToIso(now) + "\",\"nextCheckAt\":\"" + LicenseData.ToIso(now.AddDays(14)) + "\"}";
        return new SignedDocument
        {
            Payload = payload,
            Signature = Convert.ToBase64String(signer.SignData(Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)),
        };
    }

    private static ApiReply Ok(SignedDocument doc) => new() { Ok = true, License = doc };

    private static ApiReply Error(string code, int? transfersLeft = null) => new() { Ok = false, Error = code, TransfersLeft = transfersLeft };

    [Fact]
    public async Task Activate_SavesLicense_AndSurvivesRestart()
    {
        api.Reply = _ => Ok(License(Pc));
        LicenseManager m = NewManager();
        int changed = 0;
        m.Changed += (s, e) => changed++;

        LicenseActionResult r = await m.ActivateAsync("strs aaaa bbbb cccc");
        Assert.True(r.Ok);
        Assert.Equal(1, changed);
        Assert.Equal(LicenseState.Valid, m.Status.State);
        Assert.Equal("activate", api.LastAction);
        Assert.Equal("STRS-AAAA-BBBB-CCCC", api.LastRequest!.Serial);
        Assert.Equal(Pc.ToStorage(), api.LastRequest.Hwid);

        // Новый запуск CorelDRAW — лицензия читается из файла.
        Assert.Equal(LicenseState.Valid, NewManager().Status.State);
    }

    [Fact]
    public async Task BadSerial_IsRejected_WithoutServer()
    {
        LicenseActionResult r = await NewManager().ActivateAsync("12-34");
        Assert.Equal("bad_serial", r.Error);
        Assert.Null(api.LastAction);
    }

    [Fact]
    public void NormalizeSerial_AcceptsAnyFormatting()
    {
        Assert.Equal("STRS-AB2C-DE3F-GH4J", LicenseManager.NormalizeSerial(" strs-ab2c-de3f-gh4j "));
        Assert.Equal("STRS-AB2C-DE3F-GH4J", LicenseManager.NormalizeSerial("AB2CDE3FGH4J"));
        Assert.Equal("STRS-STRS-DE3F-GH4J", LicenseManager.NormalizeSerial("STRS-DE3F-GH4J"));
        Assert.Null(LicenseManager.NormalizeSerial("STRS-AB2C-DE3"));
        Assert.Null(LicenseManager.NormalizeSerial(null));
    }

    [Fact]
    public async Task Occupied_OffersTransfer_ThenTransferWorks()
    {
        api.Reply = _ => Error("occupied", 2);
        LicenseManager m = NewManager();
        LicenseActionResult r = await m.ActivateAsync("STRS-AAAA-BBBB-CCCC");
        Assert.False(r.Ok);
        Assert.True(r.CanTransfer);
        Assert.Equal(LicenseState.None, m.Status.State);

        api.Reply = action => action == "transfer" ? Ok(License(Pc)) : Error("bad_request");
        Assert.True((await m.TransferAsync("STRS-AAAA-BBBB-CCCC")).Ok);
        Assert.Equal(LicenseState.Valid, m.Status.State);

        api.Reply = _ => Error("occupied", 0);
        Assert.False((await NewManager().ActivateAsync("STRS-AAAA-BBBB-CCCC")).CanTransfer);
    }

    [Fact]
    public async Task LicenseForOtherComputer_OrForgedSignature_IsNotSaved()
    {
        api.Reply = _ => Ok(License(OtherPc));
        LicenseManager m = NewManager();
        Assert.Equal("bad_license", (await m.ActivateAsync("STRS-AAAA-BBBB-CCCC")).Error);

        SignedDocument forged = License(Pc);
        forged.Payload = forged.Payload.Replace("full", "fuls");
        api.Reply = _ => Ok(forged);
        Assert.Equal("bad_license", (await m.ActivateAsync("STRS-AAAA-BBBB-CCCC")).Error);
        Assert.False(File.Exists(m.FilePath));
    }

    [Fact]
    public async Task Trial_IsSaved_AndCountsDays()
    {
        api.Reply = action => action == "trial" ? Ok(License(Pc, "trial", Now.AddDays(14), "TRIAL")) : Error("bad_request");
        LicenseManager m = NewManager();
        Assert.True((await m.StartTrialAsync()).Ok);
        Assert.Equal(LicenseState.Trial, m.Status.State);
        Assert.Equal(14, m.Status.DaysLeft);
        Assert.Null(api.LastRequest!.Serial);

        now = Now.AddDays(15);
        Assert.Equal(LicenseState.Expired, m.Status.State);
        Assert.False(m.CanCreate);
    }

    [Fact]
    public async Task Check_OnlyWhenDue_RenewsLicense()
    {
        api.Reply = _ => Ok(License(Pc));
        LicenseManager m = NewManager();
        await m.ActivateAsync("STRS-AAAA-BBBB-CCCC");
        api.LastAction = null;

        Assert.False(await m.CheckAsync());
        Assert.Null(api.LastAction);

        now = Now.AddDays(2);
        Assert.True(m.Status.NeedsOnlineCheck);
        Assert.True(await m.CheckAsync());
        Assert.Equal("check", api.LastAction);
        Assert.False(m.Status.NeedsOnlineCheck);
    }

    [Fact]
    public async Task EveryStart_ChecksWithServer_EvenIfNotDue()
    {
        api.Reply = _ => Ok(License(Pc));
        await NewManager().ActivateAsync("STRS-AAAA-BBBB-CCCC");

        // Следующий запуск через час: сутки не прошли, но сверка всё равно идёт.
        now = Now.AddHours(1);
        api.LastAction = null;
        api.Reply = _ => Error("revoked");
        LicenseManager m = NewManager();
        Assert.True(await m.CheckOnStartAsync());
        Assert.Equal("check", api.LastAction);
        Assert.Equal(LicenseState.None, m.Status.State);
    }

    [Fact]
    public async Task EveryStart_WithoutInternet_KeepsWorking()
    {
        api.Reply = _ => Ok(License(Pc));
        await NewManager().ActivateAsync("STRS-AAAA-BBBB-CCCC");

        now = Now.AddDays(3);
        api.Reply = _ => ApiReply.Offline();
        LicenseManager m = NewManager();
        Assert.False(await m.CheckOnStartAsync());
        Assert.Equal(LicenseState.Valid, m.Status.State);
    }

    [Fact]
    public async Task EveryStart_OfflineFile_IsNotChecked()
    {
        string payload = License(Pc).Payload.TrimEnd('}') + ",\"offline\":true}";
        var doc = new SignedDocument
        {
            Payload = payload,
            Signature = Convert.ToBase64String(signer.SignData(Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)),
        };
        LicenseManager m = NewManager();
        Assert.True(m.Import(ToJson(doc)).Ok);

        api.LastAction = null;
        Assert.False(await m.CheckOnStartAsync());
        Assert.Null(api.LastAction);
    }

    [Fact]
    public async Task Check_WithoutConnection_KeepsWorkingUpTo14Days()
    {
        api.Reply = _ => Ok(License(Pc));
        LicenseManager m = NewManager();
        await m.ActivateAsync("STRS-AAAA-BBBB-CCCC");

        api.Reply = _ => ApiReply.Offline();
        now = Now.AddDays(10);
        Assert.False(await m.CheckAsync());
        Assert.Equal(LicenseState.Valid, m.Status.State);

        now = Now.AddDays(15);
        Assert.Equal(LicenseState.OfflineTooLong, m.Status.State);
        Assert.False(m.CanCreate);

        // Связь вернулась — сверка снова даёт работать.
        api.Reply = _ => Ok(License(Pc));
        Assert.True(await m.CheckAsync());
        Assert.Equal(LicenseState.Valid, m.Status.State);
    }

    [Fact]
    public async Task Check_Revoked_RemovesLicense()
    {
        api.Reply = _ => Ok(License(Pc));
        LicenseManager m = NewManager();
        await m.ActivateAsync("STRS-AAAA-BBBB-CCCC");

        api.Reply = _ => Error("revoked");
        Assert.True(await m.CheckAsync(force: true));
        Assert.Equal(LicenseState.None, m.Status.State);
        Assert.Equal("revoked", m.LastProblem);
        Assert.False(File.Exists(m.FilePath));
    }

    [Fact]
    public async Task Deactivate_FreesComputer()
    {
        api.Reply = _ => Ok(License(Pc));
        LicenseManager m = NewManager();
        await m.ActivateAsync("STRS-AAAA-BBBB-CCCC");

        api.Reply = _ => new ApiReply { Ok = true };
        Assert.True((await m.DeactivateAsync()).Ok);
        Assert.Equal("deactivate", api.LastAction);
        Assert.Equal(LicenseState.None, m.Status.State);

        api.Reply = _ => ApiReply.Offline();
        Assert.Equal("not_activated", (await m.DeactivateAsync()).Error);
    }

    [Fact]
    public void Import_OfflineFile_OnlyForThisComputer()
    {
        LicenseManager m = NewManager();
        Assert.Equal("bad_file", m.Import("not json").Error);
        Assert.Equal("wrong_computer", m.Import(ToJson(License(OtherPc))).Error);
        Assert.True(m.Import(ToJson(License(Pc))).Ok);
        Assert.Equal(LicenseState.Valid, m.Status.State);
    }

    private static string ToJson(SignedDocument doc) =>
        "{\"payload\":" + System.Text.Json.JsonSerializer.Serialize(doc.Payload) + ",\"signature\":\"" + doc.Signature + "\"}";

    private sealed class FakeApi : ILicenseApi
    {
        public Func<string, ApiReply> Reply { get; set; } = _ => ApiReply.Offline();

        public string? LastAction { get; set; }

        public ApiRequest? LastRequest { get; private set; }

        public Task<ApiReply> PostAsync(string action, ApiRequest request, CancellationToken cancel = default)
        {
            LastAction = action;
            LastRequest = request;
            return Task.FromResult(Reply(action));
        }

        public Task<ApiReply> LatestUpdateAsync(string channel, CancellationToken cancel = default) => Task.FromResult(Reply("update"));
    }
}
