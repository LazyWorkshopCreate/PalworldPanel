using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using PalworldPanel.Server.Domain;
using PalworldPanel.Server.Infrastructure;

namespace PalworldPanel.UnitTests;

public sealed class SafetyTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "palworldpanel-tests-" + Guid.NewGuid().ToString("N"));
    public SafetyTests() => Directory.CreateDirectory(root);
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(root, true);
    }

    [Theory]
    [InlineData("")]
    [InlineData("()")]
    [InlineData("( )")]
    public void EmptyObservedSettingListsCanBeValidatedAndReapplied(string value)
    {
        var field = GameSettingCatalog.Definitions.Single(d => d.Key == "DenyTechnologyList");
        GameSettingCatalog.Validate(new() { [field.Key] = value });
        Assert.Equal("", GameSettingCatalog.Encode(field, value));
        var platforms = GameSettingCatalog.Definitions.Single(d => d.Key == "CrossplayPlatforms");
        Assert.Equal("()", GameSettingCatalog.Encode(platforms, value));
    }

    [Fact]
    public void BackupsAreOrderedByCreationTimeRatherThanRandomIdentifier()
    {
        var store = new SqliteStore(Path.Combine(root, "backup-order.db"));
        var older = new BackupRecord("zzzz", "instance", root, "hash", "2026-10-06T23:00:00+08:00", 1, null, null, "image", false, false, "backup");
        var newer = older with { Id = "aaaa", CreatedUtc = "2026-10-06T16:00:00+00:00" };
        store.SaveBackup(older);
        store.SaveBackup(newer);
        Assert.Equal(new[] { "aaaa", "zzzz" }, store.Backups("instance").Select(b => b.Id));
        Assert.Equal(new[] { "aaaa", "zzzz" }, store.AllBackups().Select(b => b.Id));
    }

    [Fact]
    public void RulesCompareValuesAfterIndependentDeserialization()
    {
        var desired = new GameRules("world", Additional: new() { ["ExpRate"] = "2", ["DayTimeSpeedRate"] = "1" });
        var applied = new GameRules("world", Additional: new() { ["DayTimeSpeedRate"] = "1", ["ExpRate"] = "2" });
        Assert.True(desired.HasSameValues(applied));
        Assert.False(desired.HasSameValues(applied with { Additional = new() { ["ExpRate"] = "3", ["DayTimeSpeedRate"] = "1" } }));
        Assert.False(desired.HasSameValues(applied with { MemoryMiB = 4096 }));
        Assert.False(desired.HasSameValues(null));
        Assert.True(new GameRules("world").HasSameValues(new GameRules("world", Additional: new())));
    }

    [Fact]
    public void InstanceDirectoryCanMoveWhileLockRemainsExclusive()
    {
        var original = Path.Combine(root, "original");
        var quarantined = Path.Combine(root, "quarantined");
        var id = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(original);
        using var held = DiskLock.AcquireInstance(Path.Combine(root, "state"), original, id);
        Directory.Move(original, quarantined);
        var error = Assert.Throws<PanelException>(() => DiskLock.AcquireInstance(Path.Combine(root, "state"), quarantined, id));
        Assert.Equal("TaskConflict", error.Code);
    }

    [Fact]
    public void FinalPurgeCanRemoveDirectoryWithItsLockHeld()
    {
        var directory = Path.Combine(root, "quarantined");
        var id = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(directory);
        using var held = DiskLock.AcquireInstance(Path.Combine(root, "state"), directory, id);
        Directory.Delete(directory, true);
        Assert.False(Directory.Exists(directory));
        var error = Assert.Throws<PanelException>(() => DiskLock.AcquireInstance(Path.Combine(root, "state"), directory, id));
        Assert.Equal("InstanceDirectoryMissing", error.Code);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("a/../../outside")]
    [InlineData("C:\\outside")]
    [InlineData("a:stream")]
    public void PathsCannotEscape(string path) => Assert.Throws<PanelException>(() => SafePaths.Within(root, path));

    [Fact]
    public void WorldLookupPreservesIdentityAcrossGuidLetterCase()
    {
        var world = new string('a', 32);
        var directory = Path.Combine(root, "SaveGames", "0", world);
        Directory.CreateDirectory(directory);
        Assert.Equal(directory, WorldFiles.Find(root, world.ToUpperInvariant()));
        Assert.Throws<PanelException>(() => WorldFiles.Find(root, new string('b',32)));
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(Path.Combine(root, "SaveGames", "0", world.ToUpperInvariant()));
            Assert.Throws<PanelException>(() => WorldFiles.Find(root, world));
        }
    }

    [Fact]
    public void RestoredMetadataCannotExpandApprovedFilesystemScope()
    {
        var approved = Path.Combine(root, "instances");
        SafePaths.EnsureApproved(Path.Combine(approved, "world-a"), [approved]);
        Assert.Throws<PanelException>(() => SafePaths.EnsureApproved(approved, [approved]));
        Assert.Throws<PanelException>(() => SafePaths.EnsureApproved(Path.Combine(root, "private"), [approved]));
        Assert.Throws<PanelException>(() => SafePaths.EnsureApproved(approved + "-other/world", [approved]));
    }

    [Fact]
    public void NestedInstancesAreRejected() => Assert.Throws<PanelException>(() =>
        SafePaths.EnsureIndependent(Path.Combine(root, "parent", "child"), [Path.Combine(root, "parent")]));

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("8.8.8.8")]
    [InlineData("192.168.0.0/24")]
    [InlineData("::1")]
    public void InvalidAccessPolicyFailsClosed(string ip) => Assert.Throws<PanelException>(() => new SourceAccessPolicy([ip]));

    [Fact]
    public void AccessChecksActualSourceAndMappedAddress()
    {
        var policy = new SourceAccessPolicy(["192.168.12.34"]);
        Assert.True(policy.Allows(IPAddress.Parse("::ffff:192.168.12.34")));
        Assert.False(policy.Allows(IPAddress.Parse("192.168.12.35")));
        Assert.False(policy.Allows(null));
        Assert.Throws<PanelException>(() => new SourceAccessPolicy([]));
    }

    [Fact]
    public void LocalhostAccessDoesNotExpandLanWhitelist()
    {
        var policy = new SourceAccessPolicy(["192.168.12.34"], allowLocalhost: true);
        Assert.True(policy.Allows(IPAddress.Loopback));
        Assert.True(policy.Allows(IPAddress.Parse("::ffff:127.0.0.1")));
        Assert.True(policy.Allows(IPAddress.Parse("192.168.12.34")));
        foreach (var ip in new[] { "127.0.0.2", "::1", "192.168.12.35", "8.8.8.8" })
            Assert.False(policy.Allows(IPAddress.Parse(ip)));
        Assert.False(new SourceAccessPolicy(["192.168.12.34"]).Allows(IPAddress.Loopback));
        Assert.Throws<PanelException>(() => new SourceAccessPolicy([], allowLocalhost: true));
    }

    [Fact]
    public void LatestLogsMergeStreamsByTimestampAndKeepContinuationLines()
    {
        var logs = "2026-10-06T01:00:01.000000000Z old\r\ncontinuation\r\n2026-10-06T01:00:03.000000000Z newest\n2026-10-06T01:00:02.000000000Z stderr\n";
        Assert.Equal("2026-10-06T01:00:03.000000000Z newest\n2026-10-06T01:00:02.000000000Z stderr\n2026-10-06T01:00:01.000000000Z old\ncontinuation", LogRedactor.LatestFirst(logs));
        Assert.DoesNotContain("synthetic-password", LogRedactor.Clean(LogRedactor.LatestFirst(logs + "password=synthetic-password\n"), "synthetic-password"));
    }

    [Fact]
    public void SecretEncryptionIsPurposeBoundAndRandomized()
    {
        var path = Path.Combine(root, "key");
        File.WriteAllBytes(path, RandomNumberGenerator.GetBytes(32));
        var vault = new SecretVault(path);
        var first = vault.Seal("synthetic-value", "instance-a");
        var second = vault.Seal("synthetic-value", "instance-a");
        Assert.NotEqual(first, second);
        Assert.Equal("synthetic-value", vault.Open(first, "instance-a"));
        Assert.ThrowsAny<CryptographicException>(() => vault.Open(first, "instance-b"));
    }

    [Fact]
    public void RawEnvironmentPreservesLiteralCharactersAndUnknownContent()
    {
        const string source = "# synthetic fixture\nSERVER_NAME=old\nUNKNOWN_OPTION=keep\n";
        const string value = "synthetic $literal # comment 'single' \"double\" ";
        var patched = EnvironmentFile.Patch(source, new Dictionary<string, string> { ["SERVER_NAME"] = value });
        Assert.Contains("# synthetic fixture", patched);
        Assert.Contains("UNKNOWN_OPTION=keep", patched);
        Assert.Equal(value, EnvironmentFile.ParseRaw(patched)["SERVER_NAME"]);
        Assert.Throws<PanelException>(() => EnvironmentFile.Patch(source, new Dictionary<string, string> { ["SERVER_NAME"] = "a\nb" }));
        Assert.Throws<PanelException>(() => EnvironmentFile.ParseRaw("A=1\nA=2\n"));
    }

    [Theory]
    [InlineData("SwapPrepared")]
    [InlineData("OldRenameBeforeJournal")]
    [InlineData("OldMoved")]
    [InlineData("NewRenameBeforeJournal")]
    [InlineData("NewInstalled")]
    public void InterruptedFileSwapRetainsRecoverableOriginal(string stop)
    {
        Directory.CreateDirectory(Path.Combine(root, "Saved"));
        File.WriteAllText(Path.Combine(root, "Saved", "original"), "old");
        var swap = new SavedSwap(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(swap.Staging);
        File.WriteAllText(Path.Combine(swap.Staging, "replacement"), "new");
        Assert.Throws<IOException>(() => swap.Commit(phase => { if (phase == stop) throw new IOException("synthetic interruption"); }));
        Assert.Contains(swap.InspectRecovery(), new[] { "SwapPrepared", "OldMoved", "NewInstalled" });
        swap.Rollback();
        Assert.Equal("old", File.ReadAllText(Path.Combine(root, "Saved", "original")));
        Assert.Equal("Accepted", swap.InspectRecovery());
    }

    [Fact]
    public void IniTextEscapesParserDelimitersWithoutChangingRawEnvironment()
    {
        const string original = "literal $ # \"quote\" \\path (value),";
        var encoded = EnvironmentFile.IniText(original);
        Assert.Contains("\\\"quote\\\"", encoded);
        Assert.Equal(original, EnvironmentFile.ReadIniText(encoded));
        Assert.Equal(encoded, EnvironmentFile.ParseRaw("SERVER_NAME=" + encoded)["SERVER_NAME"]);
    }

    [Theory]
    [InlineData("FailedRenameBeforeJournal")]
    [InlineData("OriginalRestoreBeforeJournal")]
    public void RollbackInterruptionCanBeExplicitlyResumed(string phase)
    {
        Directory.CreateDirectory(Path.Combine(root, "Saved"));
        File.WriteAllText(Path.Combine(root, "Saved", "original"), "old");
        var swap = new SavedSwap(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(swap.Staging);
        File.WriteAllText(Path.Combine(swap.Staging, "replacement"), "new");
        swap.Commit();
        Assert.Throws<IOException>(() => swap.Rollback(current => { if (current == phase) throw new IOException("interruption"); }));
        swap.Rollback();
        swap.Rollback();
        Assert.Equal("old", File.ReadAllText(Path.Combine(root, "Saved", "original")));
    }

    [Fact]
    public void OperatingSystemLockExcludesSecondExecutor()
    {
        var path = Path.Combine(root, "instance.lock");
        using var first = DiskLock.Acquire(path);
        Assert.Throws<PanelException>(() => DiskLock.Acquire(path));
    }

    [Fact]
    public void LogRedactionIncludesEscapedSecretsAddressesAndPlayerIdentity()
    {
        const string secret = "synthetic\"private";
        var cleaned = LogRedactor.Clean(EnvironmentFile.IniText(secret) + "\nAuthorization: Basic synthetic\nPlayer joined: private-name\n192.168.1.2 2001:db8::1 76561198000000000", secret);
        Assert.DoesNotContain("private", cleaned);
        Assert.DoesNotContain("192.168.1.2", cleaned);
        Assert.DoesNotContain("2001:db8::1", cleaned);
        Assert.DoesNotContain("76561198000000000", cleaned);
    }

    [Fact]
    public void MissingInstanceLockDoesNotRecreateWorldDirectory()
    {
        var directory = Path.Combine(root, "missing-instance");
        Assert.Throws<PanelException>(() => DiskLock.Acquire(Path.Combine(directory, "instance.lock"), createParent: false));
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void CancellationAndClaimHaveOneWinnerAndOldIdempotencyIsStillFound()
    {
        var store = new SqliteStore(Path.Combine(root, "panel.db"));
        var original = store.Enqueue("a", "start", "{}", "admin", "request-original", "hash");
        Assert.True(store.SetTask(original.Id, "Cancelled", "Cancelled", expectedState: "Queued"));
        Assert.False(store.SetTask(original.Id, "Running", "Preflight", expectedState: "Queued"));
        Assert.Single(store.Events("a", 0));
        for (var i = 0; i < 210; i++)
        {
            var task = store.Enqueue("a", "save", "{}", "admin", "request-fill-" + i, "hash");
            store.SetTask(task.Id, "Succeeded", "Complete");
        }
        Assert.Equal(original.Id, store.FindRequest("admin", "request-original")!.Id);
        Assert.Equal("Cancelled", store.Task(original.Id).State);
    }

    [Theory]
    [InlineData("00000000:2013", 8211, true)]
    [InlineData("00000000:2014", 8211, false)]
    public void UdpSocketEvidenceMatchesActualPort(string address, int port, bool expected)
        => Assert.Equal(expected, PalworldPanel.Server.Application.InstanceService.UdpSocketPresent(" 1: " + address + " 00000000:0000 07", port));

    [Fact]
    public void DurableQueueEnforcesIdempotencyAndPerInstanceExclusion()
    {
        var store = new SqliteStore(Path.Combine(root, "panel.db"));
        var first = store.Enqueue("a", "stop", "{}", "admin", "request-0001", "hash");
        Assert.Equal(first.Id, store.Enqueue("a", "stop", "{}", "admin", "request-0001", "hash").Id);
        Assert.Throws<PanelException>(() => store.Enqueue("a", "start", "{}", "admin", "request-0001", "changed"));
        Assert.Throws<PanelException>(() => store.Enqueue("a", "start", "{}", "admin", "request-0002", "other"));
        var second = store.Enqueue("b", "start", "{}", "admin", "request-0003", "b");
        store.SetTask(first.Id, "NeedsAttention", "OldMoved");
        var reopened = new SqliteStore(Path.Combine(root, "panel.db"));
        Assert.Equal("NeedsAttention", reopened.Task(first.Id).State);
        Assert.Equal("Queued", reopened.Task(second.Id).State);
        var backup = Path.Combine(root, "snapshot.db");
        store.BackupDatabase(backup);
        Assert.Equal(2, new SqliteStore(backup).Tasks().Count);
    }

    [Fact]
    public async Task WorldArchiveExtractsOnlySelectedWorldAndPreservesAttachments()
    {
        var world = new string('a', 32);
        var zip = CreateZip([($"Saved/SaveGames/0/{world}/Level.sav", "world"),
            ($"Saved/SaveGames/0/{world}/LevelMeta.sav", "meta"),
            ($"Saved/SaveGames/0/{world}/_dps.sav", "attachment"),
            ($"Saved/SaveGames/0/{world}/Players/{new string('b', 32)}.sav", "player"),
            ("Saved/Config/LinuxServer/PalWorldSettings.ini", "source-rules")]);
        var plan = await ZipWorldArchive.InspectAsync(zip, new());
        Assert.Equal([world], plan.Worlds);
        Assert.Contains("Config", plan.Ignored);
        var destination = Path.Combine(root, "world");
        await ZipWorldArchive.ExtractWorldAsync(zip, plan, world, destination, new());
        Assert.True(File.Exists(Path.Combine(destination, "_dps.sav")));
        Assert.False(Directory.Exists(Path.Combine(destination, "Config")));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("Saved/../../outside")]
    [InlineData("/absolute")]
    [InlineData("C:/absolute")]
    [InlineData("Saved/SaveGames/0/invalid/Level.sav")]
    public async Task MaliciousArchivePathsAreRejectedBeforeExtraction(string path)
    {
        var zip = CreateZip([(path, "synthetic")]);
        await Assert.ThrowsAsync<PanelException>(() => ZipWorldArchive.InspectAsync(zip, new()));
        Assert.False(File.Exists(Path.Combine(root, "outside")));
    }

    [Fact]
    public async Task ArchiveRejectsCaseCollisionsAndExpansionLimits()
    {
        var prefix = "Saved/SaveGames/0/" + new string('a', 32) + "/";
        var zip = CreateZip([(prefix + "Level.sav", "one"), (prefix + "level.sav", "two")]);
        await Assert.ThrowsAsync<PanelException>(() => ZipWorldArchive.InspectAsync(zip, new()));
        File.Delete(zip);
        zip = CreateZip([(prefix + "Level.sav", "world"), (prefix + "LevelMeta.sav", "meta")]);
        await Assert.ThrowsAsync<PanelException>(() => ZipWorldArchive.InspectAsync(zip, new(ExpandedBytes: 2)));
    }

    private string CreateZip((string Path, string Content)[] entries)
    {
        var file = Path.Combine(root, Guid.NewGuid().ToString("N") + ".zip");
        using var archive = ZipFile.Open(file, ZipArchiveMode.Create);
        foreach (var item in entries)
        {
            using var writer = new StreamWriter(archive.CreateEntry(item.Path).Open());
            writer.Write(item.Content);
        }
        return file;
    }
}
