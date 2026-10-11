using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using PalworldPanel.Server.Application;
using PalworldPanel.Server.Domain;
using PalworldPanel.Server.Infrastructure;

namespace PalworldPanel.UnitTests;

public sealed class WorldManagementTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "palworldpanel-world-review-" + Guid.NewGuid().ToString("N"));

    public WorldManagementTests() => Directory.CreateDirectory(root);
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(root, true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ModerationRejectsAnInstanceLockOrActiveTaskBeforeContactingDocker(bool activeTask)
    {
        var id = Guid.NewGuid().ToString("N");
        var instanceRoot = Path.Combine(root, "instances", id);
        Directory.CreateDirectory(instanceRoot);
        var key = Path.Combine(root, "key");
        File.WriteAllBytes(key, RandomNumberGenerator.GetBytes(32));
        var image = "example/image@sha256:" + new string('a', 64);
        var options = new PanelOptions("192.168.1.2", 18080, ["192.168.1.3"], Path.Combine(root, "state"),
            [Path.Combine(root, "instances")], Path.Combine(root, "backups"), key, Path.Combine(root, "admin"),
            "", "", image, [image]);
        var store = new SqliteStore(Path.Combine(root, "panel.db"));
        store.SaveInstance(new InstanceRecord(id, "synthetic", instanceRoot, "project", "service", image,
            18211, 18312, 18415, "", "", new GameRules("synthetic"), Writable: true));
        var vault = new SecretVault(key);
        // A missing executable ensures these rejection paths cannot invoke a real Docker engine.
        var docker = new DockerBackend(options with { DockerExecutable = Path.Combine(root, "missing-docker") });
        var game = new GameRestClient(vault);
        var service = new InstanceService(options, store, vault, docker, game, new HostMetrics());
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.Name, "admin"),
            new Claim("authenticatedAt", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture))
        ], "test")) };
        var task = activeTask ? store.Enqueue(id, "restore", "{}", "admin", "restore-key", "restore-hash") : null;
        using (var held = activeTask ? null : DiskLock.AcquireInstance(options.StateRoot, instanceRoot, id))
        {
            var error = await Assert.ThrowsAsync<PanelException>(() => PanelEndpoints.ModerateInstanceAsync(
                id, new ModerationRequest("ban", "steam_100", null), context, service, docker, game, CancellationToken.None));
            Assert.Equal("TaskConflict", error.Code);
            Assert.False(File.Exists(Path.Combine(instanceRoot, "data", "Pal", "Saved", "SaveGames", "banlist.txt")));
        }
        if (task is not null) store.SetTask(task.Id, "Failed", "Preflight");
        // After the conflict clears, validation runs and the prior request has released its lock.
        var invalid = await Assert.ThrowsAsync<PanelException>(() => PanelEndpoints.ModerateInstanceAsync(
            id, new ModerationRequest("ban", "../unsafe", null), context, service, docker, game, CancellationToken.None));
        Assert.Equal("InvalidPlayer", invalid.Code);
    }

    [Theory]
    [InlineData("connection")]
    [InlineData("timeout")]
    [InlineData("rejected")]
    public async Task UnavailableGameReadPreservesHistoricalPlayersAndBans(string failure)
    {
        Exception error = failure switch
        {
            "connection" => new HttpRequestException("synthetic connection refusal"),
            "timeout" => new TaskCanceledException("synthetic timeout"),
            _ => new PanelException("GameApiUnavailable", "synthetic rejection")
        };
        var read = await WorldPlayers.ReadOnlineAsync(_ => Task.FromException<JsonElement>(error), CancellationToken.None);
        Assert.NotNull(read.Unavailable);
        var rows = WorldPlayers.Merge([new WorldPlayerView("steam_100", "remembered", 12, null)], read.Players, ["steam_100"], []);
        var player = Assert.Single(rows);
        Assert.True(player.Banned);
        Assert.False(player.Online);
        Assert.Equal("remembered", player.Name);
    }

    [Fact]
    public void RosterSaveIdsDoNotDecodeCorruptWorldOrPlayerBodies()
    {
        var players = Path.Combine(root, "Players");
        Directory.CreateDirectory(players);
        var id = "0123456789abcdef0123456789abcdef";
        // PlZ header followed by invalid zlib bytes: inventory decoding must not gate the roster.
        byte[] corrupt = [4, 0, 0, 0, 4, 0, 0, 0, (byte)'P', (byte)'l', (byte)'Z', 0x31, 255, 255, 255, 255];
        File.WriteAllBytes(Path.Combine(root, "Level.sav"), corrupt);
        File.WriteAllBytes(Path.Combine(players, id + ".sav"), corrupt);
        Assert.Throws<InvalidDataException>(() => SaveFiles.Read(root));
        Assert.Equal([id], SaveFiles.PlayerIds(root));
    }

    [Fact]
    public async Task CancelledRequestIsNotReportedAsAnOfflineGame()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WorldPlayers.ReadOnlineAsync(
            token => Task.FromCanceled<JsonElement>(token), cancellation.Token));
    }
}
