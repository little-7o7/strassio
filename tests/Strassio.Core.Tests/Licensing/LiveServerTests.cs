using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Strassio.Licensing;

namespace Strassio.Core.Tests.Licensing;

/// <summary>
/// Плагин ↔ настоящий сервер (server/scripts/local-server). Нужен запущенный `npm run local`; без него
/// тест ничего не делает. Запуск:
///   STRASSIO_E2E_SERVER=http://localhost:8787 STRASSIO_E2E_PUBKEY=&lt;PUBLIC_KEY из вывода сервера&gt;
///   dotnet test --filter LiveServer
/// </summary>
public class LiveServerTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "strassio-e2e-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task ActivateTransferCheckDeactivate_AgainstLocalServer()
    {
        string? server = Environment.GetEnvironmentVariable("STRASSIO_E2E_SERVER");
        string? pubkey = Environment.GetEnvironmentVariable("STRASSIO_E2E_PUBKEY");
        if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(pubkey))
        {
            return;
        }

        string serial = await CreateKey(server);
        var key = LicensePublicKey.FromBase64(pubkey);
        using var api = new HttpLicenseApi(server);
        var pc1 = HardwareCode.FromComponents(new[] { "e2e-uuid-1", "e2e-board-1", "e2e-cpu-1", "e2e-disk-1" });
        var pc2 = HardwareCode.FromComponents(new[] { "e2e-uuid-2", "e2e-board-2", "e2e-cpu-2", "e2e-disk-2" });

        var first = new LicenseManager(api, () => pc1, key, Path.Combine(dir, "pc1")) { PluginVersion = "0.6.0", CorelVersion = "27" };
        Assert.True((await first.ActivateAsync(serial.ToLowerInvariant())).Ok);
        Assert.Equal(LicenseState.Valid, first.Status.State);
        Assert.True(await first.CheckAsync(force: true));

        var second = new LicenseManager(api, () => pc2, key, Path.Combine(dir, "pc2"));
        LicenseActionResult occupied = await second.ActivateAsync(serial);
        Assert.Equal("occupied", occupied.Error);
        Assert.True(occupied.CanTransfer);
        Assert.True((await second.TransferAsync(serial)).Ok);
        Assert.Equal(LicenseState.Valid, second.Status.State);

        // Старый компьютер узнаёт об отзыве при следующей сверке.
        Assert.True(await first.CheckAsync(force: true));
        Assert.Equal(LicenseState.None, first.Status.State);
        Assert.Equal("revoked", first.LastProblem);

        Assert.True((await second.DeactivateAsync()).Ok);

        // Время выдачи — с точностью до секунды, а лицензии не позже снятой не принимаются (TimeGuard).
        // Человек за ту же секунду ключ заново не введёт, тест — может.
        await Task.Delay(1100);
        Assert.True((await first.ActivateAsync(serial)).Ok);

        var trial = new LicenseManager(api, () => pc2, key, Path.Combine(dir, "trial"));
        Assert.True((await trial.StartTrialAsync()).Ok);
        Assert.Equal(LicenseState.Trial, trial.Status.State);
        Assert.Equal(14, trial.Status.DaysLeft);

        using var offline = new HttpLicenseApi("http://127.0.0.1:1", TimeSpan.FromSeconds(2));
        Assert.Equal(ApiReply.NoConnection, (await new LicenseManager(offline, () => pc1, key, Path.Combine(dir, "off")).ActivateAsync(serial)).Error);
    }

    private static async Task<string> CreateKey(string server)
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, server.TrimEnd('/') + "/api/admin/keys")
        {
            Content = new StringContent("{\"count\":1,\"note\":\"e2e\"}", Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", Environment.GetEnvironmentVariable("STRASSIO_E2E_ADMIN") ?? "local-admin-pass");
        using HttpResponseMessage response = await http.SendAsync(request);
        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("serials")[0].GetString()!;
    }
}
