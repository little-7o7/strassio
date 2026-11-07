using System.Security.Cryptography;
using System.Text;
using Strassio.Licensing;

namespace Strassio.Core.Tests.Licensing;

/// <summary>Автообновление (SPEC 14.1): непроверенный установщик не запускается никогда, CorelDRAW ждём.</summary>
public class AutoUpdateTests : IDisposable
{
    private const string Url = "https://github.com/little-7o7/strassio-releases/releases/download/v0.9.1/Strassio-Setup-0.9.1.exe";
    private static readonly byte[] Installer = Encoding.UTF8.GetBytes("это установщик 0.9.1");
    private static readonly string GoodSha = Convert.ToHexString(SHA256.HashData(Installer)).ToLowerInvariant();

    private readonly string dir = Path.Combine(Path.GetTempPath(), "strassio-update-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>Подставная система: что скачивается, сколько раз CorelDRAW ещё открыт, что запущено.</summary>
    private sealed class Fake
    {
        public byte[] Served = Installer;
        public int Downloads;
        public int CorelOpenChecks;
        public DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);
        public string? Started;
        public string? StartedArgs;
        public bool RefuseUac;
        public List<UpdateState> SeenWhileCorelOpen = new();
        public string StatusPath = string.Empty;

        public UpdateEnvironment Env() => new(
            4242,
            (uri, path) =>
            {
                Downloads++;
                File.WriteAllBytes(path, Served);
            },
            () =>
            {
                if (CorelOpenChecks <= 0)
                {
                    return false;
                }

                CorelOpenChecks--;
                if (SeenWhileCorelOpen.Count < 5)
                {
                    SeenWhileCorelOpen.Add(UpdateStatus.Read(StatusPath)!.State);
                }

                return true;
            },
            (path, args) =>
            {
                if (RefuseUac)
                {
                    throw new UpdateCancelledException();
                }

                Started = path;
                StartedArgs = args;
            },
            span => Now += span,
            () => Now);
    }

    private (int Code, Fake Fake) Run(Action<Fake>? setup = null, string sha = "", string url = Url)
    {
        var fake = new Fake { StatusPath = Path.Combine(dir, AutoUpdate.StatusFileName) };
        setup?.Invoke(fake);
        int code = AutoUpdate.Run(url, sha.Length == 0 ? GoodSha : sha, "0.9.1", dir, fake.Env());
        return (code, fake);
    }

    private UpdateStatus Status() => UpdateStatus.Read(Path.Combine(dir, AutoUpdate.StatusFileName))!;

    [Fact]
    public void GoodDownload_WaitsForCorel_ThenStartsInstaller()
    {
        (int code, Fake fake) = Run(f => f.CorelOpenChecks = 3);

        Assert.Equal(0, code);
        Assert.Equal(1, fake.Downloads);
        Assert.Equal(new[] { UpdateState.Ready, UpdateState.Ready, UpdateState.Ready }, fake.SeenWhileCorelOpen);
        Assert.Equal(Path.Combine(dir, "Strassio-Setup-0.9.1.exe"), fake.Started);
        Assert.Contains("/SILENT", fake.StartedArgs);
        Assert.DoesNotContain("SUPPRESSMSGBOXES", fake.StartedArgs); // иначе установщик зациклится на «закройте CorelDRAW»
        Assert.Equal(UpdateState.Installing, Status().State);
        Assert.Equal(4242, Status().HelperPid);
    }

    [Fact]
    public void WrongHash_NeverStarts_AndFileIsDeleted()
    {
        (int code, Fake fake) = Run(f => f.Served = Encoding.UTF8.GetBytes("подменённый файл"));

        Assert.Equal(2, code);
        Assert.Null(fake.Started);
        Assert.Equal(UpdateState.Failed, Status().State);
        Assert.Equal("sha256", Status().Detail);
        Assert.Empty(Directory.GetFiles(dir, "*.exe*"));
    }

    [Fact]
    public void FileSwappedWhileWaiting_NeverStarts()
    {
        var fake = new Fake { StatusPath = Path.Combine(dir, AutoUpdate.StatusFileName), CorelOpenChecks = 1 };
        UpdateEnvironment inner = fake.Env();
        var env = new UpdateEnvironment(
            inner.ProcessId,
            inner.Download,
            () =>
            {
                bool open = inner.IsCorelRunning();
                if (open)
                {
                    File.WriteAllText(Path.Combine(dir, "Strassio-Setup-0.9.1.exe"), "чужой файл");
                }

                return open;
            },
            inner.StartInstaller,
            inner.Sleep,
            inner.UtcNow);

        int code = AutoUpdate.Run(Url, GoodSha, "0.9.1", dir, env);

        Assert.Equal(2, code);
        Assert.Null(fake.Started);
        Assert.Equal("sha256", Status().Detail);
    }

    [Fact]
    public void AlreadyDownloaded_IsNotDownloadedAgain()
    {
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "Strassio-Setup-0.9.1.exe"), Installer);

        (int code, Fake fake) = Run();

        Assert.Equal(0, code);
        Assert.Equal(0, fake.Downloads);
        Assert.NotNull(fake.Started);
    }

    [Fact]
    public void UacRefused_IsFailedCancelled()
    {
        (int code, Fake fake) = Run(f => f.RefuseUac = true);

        Assert.Equal(2, code);
        Assert.Equal(UpdateState.Failed, Status().State);
        Assert.Equal("cancelled", Status().Detail);
    }

    [Fact]
    public void CorelNeverClosed_GivesUpAfterAWeek()
    {
        (int code, Fake fake) = Run(f => f.CorelOpenChecks = int.MaxValue);

        Assert.Equal(2, code);
        Assert.Null(fake.Started);
        Assert.Equal("timeout", Status().Detail);
        Assert.True(fake.Now - new DateTime(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc) >= AutoUpdate.MaxWaitForCorel);
    }

    [Theory]
    [InlineData("http://example.com/Strassio-Setup.exe", "")]   // не https
    [InlineData(Url, "abc")]                                     // не SHA-256
    [InlineData(Url, "zz66666666666666666666666666666666666666666666666666666666666666")]
    public void BadInput_DoesNothing(string url, string sha)
    {
        (int code, Fake fake) = Run(sha: sha.Length == 0 ? GoodSha : sha, url: url);

        Assert.Equal(1, code);
        Assert.Equal(0, fake.Downloads);
        Assert.Null(fake.Started);
    }

    [Theory]
    [InlineData("0.9.1", "Strassio-Setup-0.9.1.exe")]
    [InlineData("1.0.0-beta.1", "Strassio-Setup-1.0.0-beta.1.exe")]
    [InlineData(@"..\..\Windows\evil", "Strassio-Setup-Windowsevil.exe")]
    [InlineData("", "Strassio-Setup-update.exe")]
    public void InstallerName_HasNoPathParts(string version, string expected) =>
        Assert.Equal(expected, AutoUpdate.InstallerName(version));

    [Fact]
    public void Status_SurvivesRoundTrip_AndGarbageIsNull()
    {
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "status.txt");
        new UpdateStatus { State = UpdateState.Failed, Version = "0.9.1", HelperPid = 17, Detail = "строка\nс переносом" }.Write(path);

        UpdateStatus back = UpdateStatus.Read(path)!;
        Assert.Equal(UpdateState.Failed, back.State);
        Assert.Equal("0.9.1", back.Version);
        Assert.Equal(17, back.HelperPid);
        Assert.Equal("строка с переносом", back.Detail);

        Assert.Contains("state=failed", File.ReadAllText(path)); // строкой, не номером: обфускатор не испортит
        File.WriteAllText(path, "state=7\nversion=0.9.1");
        Assert.Null(UpdateStatus.Read(path));
        File.WriteAllText(path, "что-то другое");
        Assert.Null(UpdateStatus.Read(path));
        Assert.Null(UpdateStatus.Read(Path.Combine(dir, "нет.txt")));
    }

    // Состояния передаются строками: сами перечисления внутренние, а методы тестов — открытые.
    [Theory]
    [InlineData("Downloading", true, "0.9.0", "Downloading")]
    [InlineData("Downloading", false, "0.9.0", "None")]   // компьютер перезагрузили — начать заново
    [InlineData("Ready", true, "0.9.0", "Ready")]
    [InlineData("Ready", false, "0.9.0", "None")]
    [InlineData("Installing", false, "0.9.1", "Updated")]
    [InlineData("Installing", false, "0.9.0", "None")]    // установку прервали
    [InlineData("Failed", false, "0.9.0", "Failed")]
    public void Describe_WhatTheDockerShows(string state, bool alive, string current, string expected)
    {
        var status = new UpdateStatus { State = Enum.Parse<UpdateState>(state), Version = "0.9.1", HelperPid = 5 };
        Assert.Equal(Enum.Parse<UpdateProgress>(expected), AutoUpdate.Describe(status, current, pid => alive && pid == 5));
    }

    [Fact]
    public void Sha256_Checks()
    {
        Assert.True(AutoUpdate.IsSha256(GoodSha));
        Assert.True(AutoUpdate.IsSha256(GoodSha.ToUpperInvariant()));
        Assert.False(AutoUpdate.IsSha256(null));
        Assert.False(AutoUpdate.IsSha256(GoodSha + "0"));
    }
}
