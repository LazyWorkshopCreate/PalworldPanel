using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PalworldPanel.Server.Application;
using PalworldPanel.Server.Domain;
using PalworldPanel.Server.Infrastructure;

namespace PalworldPanel.UnitTests;

public sealed class WorldReadOnlyTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "pp-world-readonly-" + Guid.NewGuid().ToString("N"));
    private readonly SqliteStore store;
    private readonly SecretVault vault;
    private readonly PanelOptions options;
    private readonly InstanceService instances;
    private readonly WorldReadOnlySecurity security;
    private readonly Clock clock = new();
    private readonly InstanceRecord instance;
    private sealed class Clock : TimeProvider { public DateTimeOffset Now = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Now; }
    public WorldReadOnlyTests()
    {
        Directory.CreateDirectory(root);
        var key = Path.Combine(root, "key"); File.WriteAllBytes(key, RandomNumberGenerator.GetBytes(32));
        vault = new(key); store = new(Path.Combine(root, "panel.db"));
        options = new("192.168.1.2", 18080, ["192.168.1.3"], Path.Combine(root, "state"), [Path.Combine(root, "instances")], Path.Combine(root, "backups"), key, "", "", "", "image", ["image"]);
        instances = new(options, store, vault, null!, null!, null!);
        security = new(store, vault, clock);
        var rules = new GameRules("synthetic", Additional: new() { ["ExpRate"] = "2", ["PublicIP"] = "192.168.1.99", ["AdminPassword"] = "synthetic-leak" });
        instance = new("11111111111111111111111111111111", "synthetic", Path.Combine(root, "instances", "11111111111111111111111111111111"), "project", "palworld", "image", 18211, 18312, 18415,
            vault.Seal("synthetic-admin", "11111111111111111111111111111111:admin"), vault.Seal("synthetic-game", "11111111111111111111111111111111:game"), rules, rules, WorldGuid: new string('a', 32));
        Directory.CreateDirectory(instance.Root); store.SaveInstance(instance);
    }
    [Fact]
    public void AppliedPasswordOnlyAndSessionRevocation()
    {
        var token = security.Login(instance.WorldGuid, "synthetic-game", "source");
        var changed = instance with { AppliedGameCipher = instance.GameCipher, GameCipher = vault.Seal("synthetic-draft", "11111111111111111111111111111111:game") };
        store.SaveInstance(changed);
        Assert.Equal(instance.Id, security.Require(token, instance.WorldGuid!).Id);
        Assert.Throws<PanelException>(() => security.Login(instance.WorldGuid, "synthetic-draft", "source"));
        store.SaveInstance(changed with { AppliedGameCipher = changed.GameCipher });
        Assert.Equal(401, Assert.Throws<PanelException>(() => security.Require(token, instance.WorldGuid!)).Status);
    }
    [Fact]
    public void CrossWorldExpiryLogoutRestartAndWorldReplacementDeny()
    {
        var token = security.Login(instance.WorldGuid, "synthetic-game", "source");
        Assert.Throws<PanelException>(() => security.Require(token, new string('b', 32)));
        Assert.Throws<PanelException>(() => new WorldReadOnlySecurity(store, vault).Require(token, instance.WorldGuid!));
        clock.Now = clock.Now.AddMinutes(31);
        Assert.Throws<PanelException>(() => security.Require(token, instance.WorldGuid!));
        token = security.Login(instance.WorldGuid, "synthetic-game", "source");
        security.Logout(token); Assert.Throws<PanelException>(() => security.Require(token, instance.WorldGuid!));
        token = security.Login(instance.WorldGuid, "synthetic-game", "source");
        store.SaveInstance(instance with { WorldGuid = new string('b', 32) });
        Assert.Throws<PanelException>(() => security.Require(token, instance.WorldGuid!));
    }
    [Theory]
    [InlineData("empty")][InlineData("unknown")][InlineData("quarantine")][InlineData("purged")][InlineData("duplicate")][InlineData("unapplied")]
    public void UnavailableWorldsUseSameDenial(string mode)
    {
        var modified = mode switch {
            "empty" => instance with { GameCipher = vault.Seal("", "11111111111111111111111111111111:game") },
            "unknown" => instance with { WorldGuid = null },
            "quarantine" => instance with { QuarantinedUtc = "now" },
            "purged" => instance with { PurgedUtc = "now" },
            "unapplied" => instance with { Applied = null }, _ => instance
        };
        store.SaveInstance(modified);
        if (mode == "duplicate") store.SaveInstance(instance with { Id = "22222222222222222222222222222222", Root = Path.Combine(root, "instances", "22222222222222222222222222222222"), GamePort = 18212, RestPort = 18313, QueryPort = 18416 });
        var error = Assert.Throws<PanelException>(() => security.Login(instance.WorldGuid, "synthetic-game", "source"));
        Assert.Equal("WorldAccessDenied", error.Code); Assert.Equal(401, error.Status);
    }
    [Fact]
    public void RateLimitIncludesUnknownWorldAndExpires()
    {
        for (var i = 0; i < 5; i++) Assert.Throws<PanelException>(() => security.Login("unknown", "wrong", "source"));
        Assert.Equal(429, Assert.Throws<PanelException>(() => security.Login(instance.WorldGuid, "synthetic-game", "source")).Status);
        Assert.NotEmpty(security.Login(instance.WorldGuid, "synthetic-game", "other-source"));
        clock.Now = clock.Now.AddMinutes(16);
        Assert.NotEmpty(security.Login(instance.WorldGuid, "synthetic-game", "source"));
        Assert.Equal(instance.WorldGuid, WorldReadOnlySecurity.Normalize(Guid.ParseExact(instance.WorldGuid!, "N").ToString()));
    }
    [Fact]
    public void SafeSettingsAndLogsHideSecretsAndHostData()
    {
        using var json = JsonDocument.Parse("{\"AdminPassword\":\"secret\",\"PublicIP\":\"private\",\"ExpRate\":2,\"ServerName\":\"synthetic\"}");
        var settings = WorldReadOnlyEndpoints.Settings(instance.Applied!, json.RootElement);
        Assert.DoesNotContain(settings, s => s.Key is "AdminPassword" or "ServerPassword" or "PublicIP" or "PublicPort" or "RESTAPIPort" or "BanListURL");
        Assert.Equal("2", settings.Single(s => s.Key == "ExpRate").Observed);
        var logs = WorldReadOnlyEndpoints.CleanLogs("synthetic-admin synthetic-game " + instance.Root + " /home/user/file /palworld/private/file 192.168.1.99\nnormal log", instance, vault);
        Assert.DoesNotContain("synthetic-admin", logs); Assert.DoesNotContain("synthetic-game", logs); Assert.DoesNotContain(instance.Root, logs); Assert.DoesNotContain("/home/user", logs); Assert.DoesNotContain("/palworld", logs); Assert.DoesNotContain("192.168", logs); Assert.Contains("normal log", logs);
    }
    [Theory]
    [InlineData("[\"Steam\",\"PS5\",\"Xbox\",\"Mac\"]", "(Steam,PS5,Xbox,Mac)")]
    [InlineData("[]", "()")]
    [InlineData("[{}]", null)]
    [InlineData("[null]", null)]
    public void RealGameArraySettingsAreProjectedWithoutExposingNestedObjects(string array, string? expected)
    {
        using var json = JsonDocument.Parse("{\"CrossplayPlatforms\":" + array + ",\"DenyTechnologyList\":[],\"AdminPassword\":[\"secret\"],\"ServerName\":[\"invalid\"]}");
        var settings = WorldReadOnlyEndpoints.Settings(instance.Applied!, json.RootElement);
        Assert.Equal(expected, settings.Single(s => s.Key == "CrossplayPlatforms").Observed);
        Assert.Equal("()", settings.Single(s => s.Key == "DenyTechnologyList").Observed);
        Assert.Null(settings.Single(s => s.Key == "ServerName").Observed);
        Assert.DoesNotContain(settings, s => s.Key == "AdminPassword");
    }
    [Fact]
    public void RealContainerAnsiColoursDoNotLeaveControlFragmentsInVisitorLogs()
    {
        var logs = WorldReadOnlyEndpoints.CleanLogs("\u001b[1;36m****STARTING SERVER****\u001b[0m\n\u001b[32msynthetic-game\u001b[0m", instance, vault);
        Assert.Contains("****STARTING SERVER****", logs);
        Assert.Contains("[redacted]", logs);
        Assert.DoesNotContain("[1;36m", logs);
        Assert.DoesNotContain("[0m", logs);
        Assert.False(logs.Contains('\u001b'));
        Assert.DoesNotContain("synthetic-game", logs);
    }
    private async Task<BackupRecord> Backup(bool installation = false)
    {
        var world = Path.Combine(instance.Root, "data", "Pal", "Saved", "SaveGames", "0", instance.WorldGuid!);
        Directory.CreateDirectory(Path.Combine(world, "Players"));
        File.WriteAllText(Path.Combine(world, "Level.sav"), "synthetic-world"); File.WriteAllText(Path.Combine(world, "LevelMeta.sav"), "synthetic-meta");
        File.WriteAllText(Path.Combine(world, "Players", new string('c', 32) + ".sav"), "synthetic-player");
        File.WriteAllText(Path.Combine(world, "unexpected.txt"), "excluded");
        Directory.CreateDirectory(Path.Combine(instance.Root, "data", "Pal", "Saved", "Config"));
        File.WriteAllText(Path.Combine(instance.Root, "data", "Pal", "Saved", "Config", "private.ini"), "synthetic-admin");
        foreach (var file in new[] { "compose.yaml", "settings.env", "secrets.env" }) File.WriteAllText(Path.Combine(instance.Root, file), "synthetic-admin");
        Directory.CreateDirectory(options.BackupRoot);
        return await new BackupService(options, store, instances).CreateAsync(instance, "manual", installation);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task DownloadIncludesOnlyWorldSavesAndDeletesTemporaryFile(bool installation)
    {
        var backup = await Backup(installation);
        var export = new WorldSaveExport(options, store, instances, new HeavyIoGate());
        string temporary;
        await using (var stream = await export.ExportAsync(instance, backup.Id, CancellationToken.None))
        {
            temporary = stream.Name;
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, true);
            Assert.Equal(3, archive.Entries.Count);
            Assert.All(archive.Entries, e => { Assert.StartsWith("Saved/SaveGames/0/" + instance.WorldGuid + "/", e.FullName); Assert.EndsWith(".sav", e.FullName); });
            Assert.Contains(archive.Entries, e => e.FullName.Contains("Players/"));
        }
        Assert.False(File.Exists(temporary));
    }
    [Theory][InlineData("corrupt")][InlineData("lock")][InlineData("task")][InlineData("world")][InlineData("owner")][InlineData("path")]
    public async Task DownloadRejectsCorruptionConflictAndWrongOwnership(string mode)
    {
        var backup = await Backup();
        if (mode == "corrupt") File.AppendAllText(Path.Combine(backup.Path, "Saved", "SaveGames", "0", instance.WorldGuid!, "Level.sav"), "changed");
        if (mode is "world" or "owner" or "path") store.ForgetBackup(backup.Id);
        if (mode == "owner") store.SaveInstance(instance with { Id = "22222222222222222222222222222222", Root = Path.Combine(root, "instances", "two"), GamePort = 18212, RestPort = 18313, QueryPort = 18416 });
        if (mode == "world") store.SaveBackup(backup with { WorldGuid = new string('b', 32) });
        if (mode == "owner") store.SaveBackup(backup with { InstanceId = "22222222222222222222222222222222" });
        if (mode == "path") store.SaveBackup(backup with { Path = root });
        if (mode == "task") store.Enqueue(instance.Id, "restore", "{}", "admin", "readonly-restore-key", "hash");
        using var held = mode == "lock" ? DiskLock.AcquireInstance(options.StateRoot, instance.Root, instance.Id) : null;
        await Assert.ThrowsAsync<PanelException>(() => new WorldSaveExport(options, store, instances, new HeavyIoGate()).ExportAsync(instance, backup.Id, CancellationToken.None));
        Assert.False(Directory.Exists(Path.Combine(options.StateRoot, "exports")) && Directory.EnumerateFiles(Path.Combine(options.StateRoot, "exports")).Any());
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
}
