using System.IO.Compression;
using PalworldPanel.Server.Application;
using PalworldPanel.Server.Domain;
using PalworldPanel.Server.Infrastructure;
using Xunit;

namespace PalworldPanel.UnitTests;

public sealed class AuthenticationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "palworldpanel-auth-" + Guid.NewGuid().ToString("N"));
    public AuthenticationTests() => Directory.CreateDirectory(root);
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(root, true);
    }

    [Theory]
    [InlineData(false, "172.30.88.1", "172.30.88.5")]
    [InlineData(true, "192.168.1.2", "172.30.88.5")]
    [InlineData(true, "172.30.88.1", "192.168.1.3")]
    public void HttpCannotEnableOutsideDedicatedDesktopFixture(bool desktop, string bind, string source)
    {
        var options = new PanelOptions(bind, 18080, [source], root, [root], root,
            "missing-key", "missing-admin", "missing-cert", "missing-password", "image", ["image"],
            DesktopValidation: desktop, DesktopAllowHttp: true);
        Assert.Equal("InvalidConfiguration", Assert.Throws<PanelException>(options.Validate).Code);
        Assert.Empty(Directory.GetFileSystemEntries(root));
    }

    [Fact]
    public async Task ExplicitHttpRequiresNoTlsFilesButKeepsPrivateAccessPolicy()
    {
        var key = Path.Combine(root, "key");
        var admin = Path.Combine(root, "admin");
        File.WriteAllText(key, "synthetic");
        File.WriteAllText(admin, "synthetic");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(key, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.SetUnixFileMode(admin, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        var image = "example/image@sha256:" + new string('a', 64);
        var options = new PanelOptions("192.168.1.2", 18080, ["192.168.1.3"],
            Path.Combine(root, "state"), [Path.Combine(root, "instances")], Path.Combine(root, "backups"),
            key, admin, "", "", image, [image], AllowHttp: true);
        options.Validate();
        Assert.True(options.UsesHttp);
        Assert.Equal("InvalidAccessPolicy", Assert.Throws<PanelException>(() =>
            (options with { AllowedIps = ["8.8.8.8"] }).Validate()).Code);
        Assert.Equal("InvalidConfiguration", Assert.Throws<PanelException>(() =>
            (options with { AllowHttp = false }).Validate()).Code);
        var store = new SqliteStore(Path.Combine(options.StateRoot, "panel.db"));
        var backups = new PanelBackupService(options, store, new HeavyIoGate());
        await using var export = await backups.ExportAsync("synthetic-export-passphrase", CancellationToken.None);
        using var plaintext = new MemoryStream();
        await EncryptedArchive.DecryptAsync(export, plaintext, "synthetic-export-passphrase");
        plaintext.Position = 0;
        using var archive = new ZipArchive(plaintext, ZipArchiveMode.Read);
        Assert.NotNull(archive.GetEntry("private/master-key"));
        Assert.NotNull(archive.GetEntry("private/administrator.json"));
        Assert.Null(archive.GetEntry("private/tls.pfx"));
    }

    [Fact]
    public void WebsiteSetupIsExplicitOneTimeAndSurvivesRestart()
    {
        var path = Path.Combine(root, "website-admin.json");
        AdministratorSecurity.PrepareWebSetup(path);
        Assert.Throws<PanelException>(() => new AdministratorSecurity(path));
        var security = new AdministratorSecurity(path, allowWebSetup: true);
        Assert.True(security.SetupRequired);
        var token = security.SetupToken!;
        Assert.Equal("InvalidSetupToken", Assert.Throws<PanelException>(() => security.CompleteWebSetup("wrong", "synthetic-setup-password", "synthetic-setup-password")).Code);
        Assert.Equal("WeakCredential", Assert.Throws<PanelException>(() => security.CompleteWebSetup(token, "short", "short")).Code);
        Assert.Equal("WeakCredential", Assert.Throws<PanelException>(() => security.CompleteWebSetup(token, "synthetic-setup-password", "different")).Code);
        security.CompleteWebSetup(token, "synthetic-setup-password", "synthetic-setup-password");
        Assert.False(security.SetupRequired);
        Assert.Null(security.SetupToken);
        Assert.DoesNotContain("synthetic-setup-password", File.ReadAllText(path));
        Assert.Equal("AdministratorExists", Assert.Throws<PanelException>(() => security.CompleteWebSetup(token, "replacement-password", "replacement-password")).Code);
        var restarted = new AdministratorSecurity(path, allowWebSetup: true);
        Assert.False(restarted.SetupRequired);
        Assert.True(restarted.Authenticate("admin", "synthetic-setup-password", "192.168.1.3").Identity!.IsAuthenticated);
        File.Delete(path);
        Assert.Throws<FileNotFoundException>(() => new AdministratorSecurity(path, allowWebSetup: true));
    }

    [Fact]
    public void FailedLoginsLockAccountAcrossAllowedSources()
    {
        var path = Path.Combine(root, "administrator.json");
        AdministratorSecurity.Initialize(path, "admin", "synthetic-test-password-only");
        var security = new AdministratorSecurity(path);
        for (var i = 0; i < 5; i++)
            Assert.Equal("LoginFailed", Assert.Throws<PanelException>(() => security.Authenticate("admin", "wrong", "10.1.1.1")).Code);
        Assert.Equal("LoginLimited", Assert.Throws<PanelException>(() => security.Authenticate("admin", "synthetic-test-password-only", "10.1.1.2")).Code);
    }

    [Fact]
    public void ConfirmationIsSingleUseAndBoundToRevisionAndUser()
    {
        var tokens = new ConfirmationTokens();
        var first = tokens.Issue("admin", "instance", "restore", 3, "hash");
        Assert.Throws<PanelException>(() => tokens.Consume(first.Token, "admin", "instance", "restore", 4, "hash"));
        Assert.Throws<PanelException>(() => tokens.Consume(first.Token, "admin", "instance", "restore", 3, "hash"));
        var next = tokens.Issue("admin", "instance", "restore", 3, "hash");
        tokens.Consume(next.Token, "admin", "instance", "restore", 3, "hash");
        Assert.Throws<PanelException>(() => tokens.Consume(next.Token, "admin", "instance", "restore", 3, "hash"));
    }
}
