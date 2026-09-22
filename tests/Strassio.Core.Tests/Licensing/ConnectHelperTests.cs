using System.Net;
using System.Net.Sockets;
using System.Text;
using Strassio.Licensing;

namespace Strassio.Core.Tests.Licensing;

/// <summary>
/// Связь через Strassio.Connect.exe — когда CorelDRAW закрыт брандмауэром и прямой запрос из плагина
/// не проходит. Нужен собранный helper (dotnet build src/Strassio.Connect); нет файла — тест пропускает проверку.
/// </summary>
public class ConnectHelperTests
{
    private static string? HelperPath()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "Strassio.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        if (dir == null)
        {
            return null;
        }

        return new[] { "Debug", "Release" }
            .Select(c => Path.Combine(dir, "src", "Strassio.Connect", "bin", c, "net48", "Strassio.Connect.exe"))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    [Fact]
    public async Task Post_GoesThroughHelper_WhenDirectIsNotUsed()
    {
        string? helper = HelperPath();
        if (helper == null)
        {
            return;
        }

        int port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        string? receivedBody = null;
        string? receivedPath = null;
        Task server = Task.Run(async () =>
        {
            HttpListenerContext ctx = await listener.GetContextAsync();
            receivedPath = ctx.Request.Url!.AbsolutePath;
            using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
            {
                receivedBody = await reader.ReadToEndAsync();
            }

            byte[] answer = Encoding.UTF8.GetBytes("{\"ok\":false,\"error\":\"revoked\"}");
            ctx.Response.StatusCode = 403;
            await ctx.Response.OutputStream.WriteAsync(answer);
            ctx.Response.Close();
        });

        using var api = new HttpLicenseApi($"http://127.0.0.1:{port}", helperPath: helper, tryDirect: false);
        ApiReply reply = await api.PostAsync("check", new ApiRequest { Serial = "STRS-AAAA-BBBB-CCCC", Hwid = "a.b.c.d" });
        await server;

        Assert.Equal("/api/check", receivedPath);
        Assert.Contains("\"serial\":\"STRS-AAAA-BBBB-CCCC\"", receivedBody);
        Assert.False(reply.Ok);
        Assert.Equal("revoked", reply.Error);
        Assert.Null(api.LastError);
    }

    [Fact]
    public async Task HelperFailure_IsNoConnection_WithReason()
    {
        string? helper = HelperPath();
        if (helper == null)
        {
            return;
        }

        using var api = new HttpLicenseApi($"http://127.0.0.1:{FreePort()}", helperPath: helper, tryDirect: false);
        ApiReply reply = await api.PostAsync("check", new ApiRequest { Hwid = "a.b.c.d" });
        Assert.Equal(ApiReply.NoConnection, reply.Error);
        Assert.StartsWith("Strassio.Connect: ", api.LastError);
    }

    [Fact]
    public async Task NoHelperFile_DirectErrorIsReported()
    {
        using var api = new HttpLicenseApi($"http://127.0.0.1:{FreePort()}", helperPath: @"C:\нет\Strassio.Connect.exe");
        ApiReply reply = await api.PostAsync("check", new ApiRequest { Hwid = "a.b.c.d" });
        Assert.Equal(ApiReply.NoConnection, reply.Error);
        Assert.NotNull(api.LastError);
        Assert.DoesNotContain("Strassio.Connect:", api.LastError);
    }
}
