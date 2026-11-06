using System.Security.Cryptography;
using System.Text;
using Strassio.Licensing;

namespace Strassio.Core.Tests.Licensing;

/// <summary>Уведомление о новой версии (SPEC 14.1): подпись сервера, сравнение версий, ссылка.</summary>
public class UpdateCheckerTests : IDisposable
{
    private readonly ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly LicensePublicKey key;

    public UpdateCheckerTests()
    {
        ECParameters p = signer.ExportParameters(false);
        key = LicensePublicKey.FromBase64(Convert.ToBase64String(p.Q.X!.Concat(p.Q.Y!).ToArray()));
    }

    public void Dispose() => signer.Dispose();

    /// <summary>Тот же вид, что подписывает сервер (server/core/service.ts, latestUpdate).</summary>
    private SignedDocument Doc(string version, string url = "https://github.com/little-7o7/strassio-releases/releases/download/v0.9.0/Strassio-Setup-0.9.0.exe", string? minVersion = "")
    {
        string min = minVersion == null ? "null" : "\"" + minVersion + "\"";
        string payload = "{\"version\":\"" + version + "\",\"url\":\"" + url + "\",\"sha256\":\"abc\",\"notesRu\":\"Что нового\",\"notesEn\":\"\",\"minVersion\":" + min + "}";
        return Sign(payload, signer);
    }

    private static SignedDocument Sign(string payload, ECDsa by) => new()
    {
        Payload = payload,
        Signature = Convert.ToBase64String(by.SignData(Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)),
    };

    private Task<UpdateInfo?> Check(ApiReply reply, string current = "0.8.3") =>
        new UpdateChecker(new FakeApi(reply), key, current).CheckAsync();

    [Fact]
    public async Task NewerVersion_IsOffered()
    {
        UpdateInfo? info = await Check(new ApiReply { Ok = true, Update = Doc("0.9.0") });
        Assert.NotNull(info);
        Assert.Equal("0.9.0", info!.Version);
        Assert.Equal("Что нового", info.Notes("ru"));
        Assert.Equal("Что нового", info.Notes("en")); // английского текста нет — показываем русский
        Assert.False(info.IsRequiredFor("0.8.3"));
    }

    [Fact]
    public async Task SameOrOlderVersion_IsNotOffered()
    {
        Assert.Null(await Check(new ApiReply { Ok = true, Update = Doc("0.8.3") }));
        Assert.Null(await Check(new ApiReply { Ok = true, Update = Doc("0.8.2") }));
        Assert.Null(await Check(new ApiReply { Ok = true, Update = Doc("0.10.0") }, current: "1.0.0"));
    }

    [Fact]
    public async Task ForgedOrBrokenAnswers_AreIgnored()
    {
        // Подпись чужим ключом (подставной сервер).
        using ECDsa other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        SignedDocument forged = Sign(Doc("9.9.9").Payload, other);
        Assert.Null(await Check(new ApiReply { Ok = true, Update = forged }));

        // Подпись верная, но текст поправлен после подписи.
        SignedDocument edited = Doc("0.9.0");
        edited.Payload = edited.Payload.Replace("0.9.0", "9.9.9");
        Assert.Null(await Check(new ApiReply { Ok = true, Update = edited }));

        // Ссылка не https — не открываем.
        Assert.Null(await Check(new ApiReply { Ok = true, Update = Doc("0.9.0", url: "http://example.com/setup.exe") }));
        Assert.Null(await Check(new ApiReply { Ok = true, Update = Doc("0.9.0", url: "C:\\\\Windows\\\\calc.exe") }));

        // Нет связи, нет обновления на сервере, ерунда вместо версии.
        Assert.Null(await Check(ApiReply.Offline()));
        Assert.Null(await Check(new ApiReply { Ok = false, Error = "no_update" }));
        Assert.Null(await Check(new ApiReply { Ok = true, Update = Doc("скоро") }));
        Assert.Null(await Check(new ApiReply { Ok = true, Update = Doc("0.9.0") }, current: ""));
    }

    [Fact]
    public async Task NetworkException_IsSwallowed()
    {
        var checker = new UpdateChecker(new ThrowingApi(), key, "0.8.3");
        Assert.Null(await checker.CheckAsync());
    }

    [Fact]
    public async Task MinVersion_MarksUpdateAsRequired()
    {
        UpdateInfo? info = await Check(new ApiReply { Ok = true, Update = Doc("1.0.0", minVersion: "0.9.0") });
        Assert.NotNull(info);
        Assert.True(info!.IsRequiredFor("0.8.3"));
        Assert.False(info.IsRequiredFor("0.9.0"));

        // minVersion: null (так его может прислать база) — не обязательное.
        UpdateInfo? noMin = await Check(new ApiReply { Ok = true, Update = Doc("1.0.0", minVersion: null) });
        Assert.NotNull(noMin);
        Assert.False(noMin!.IsRequiredFor("0.8.3"));
    }

    [Theory]
    [InlineData("0.9.0", "0.8.3", true)]
    [InlineData("0.10.0", "0.9.9", true)]
    [InlineData("1.0", "0.9.9", true)]
    [InlineData("v1.0.1", "1.0.0", true)]
    [InlineData("1.0.0", "1.0.0-beta.2", true)]
    [InlineData("1.0.0-beta.2", "1.0.0", false)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("0.9.0", "1.0.0", false)]
    [InlineData("", "1.0.0", false)]
    [InlineData("abc", "1.0.0", false)]
    public void IsNewer(string candidate, string current, bool expected) =>
        Assert.Equal(expected, UpdateChecker.IsNewer(candidate, current));

    private sealed class FakeApi : ILicenseApi
    {
        private readonly ApiReply reply;

        public FakeApi(ApiReply reply) => this.reply = reply;

        public Task<ApiReply> PostAsync(string action, ApiRequest request, CancellationToken cancel = default) => Task.FromResult(ApiReply.Offline());

        public Task<ApiReply> LatestUpdateAsync(string channel, CancellationToken cancel = default) => Task.FromResult(reply);
    }

    private sealed class ThrowingApi : ILicenseApi
    {
        public Task<ApiReply> PostAsync(string action, ApiRequest request, CancellationToken cancel = default) => throw new HttpRequestException("нет сети");

        public Task<ApiReply> LatestUpdateAsync(string channel, CancellationToken cancel = default) => throw new HttpRequestException("нет сети");
    }
}
