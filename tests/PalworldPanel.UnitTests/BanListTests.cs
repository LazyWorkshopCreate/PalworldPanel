using Microsoft.Data.Sqlite;
using PalworldPanel.Server.Domain;
using PalworldPanel.Server.Infrastructure;

namespace PalworldPanel.UnitTests;

public sealed class BanListTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "palworldpanel-access-" + Guid.NewGuid().ToString("N"));
    public BanListTests() => Directory.CreateDirectory(root);
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(root, true);
    }

    [Fact]
    public void BanListRejectsUnsafeIds()
    {
        var saveGames = Path.Combine(root, "SaveGames");
        Directory.CreateDirectory(saveGames);
        ServerBanList.Write(saveGames, ["steam_100"]);
        Assert.Equal("steam_100", Assert.Single(ServerBanList.Read(saveGames)));
        Assert.False(ServerBanList.IsUserId("../etc"));
        Assert.Equal("InvalidPlayer", Assert.Throws<PanelException>(() => ServerBanList.Parse("steam_100\nsteam_100\n")).Code);
    }

    [Fact]
    public void OnlineProjectionKeepsSaveIdAndDropsAddress()
    {
        using var document = System.Text.Json.JsonDocument.Parse("""
            {"players":[
              {"name":"艾拉\u0000","userId":"steam_100","playerId":"0123456789ABCDEF0123456789ABCDEF","ip":"10.0.0.8","level":12,"accountName":"hidden"},
              {"name":"坏编号","userId":"../etc","playerId":"0123456789abcdef0123456789abcdef"}
            ]}
            """);
        var player = Assert.Single(WorldPlayers.Project(document.RootElement));
        Assert.Equal("steam_100", player.UserId);
        Assert.Equal("艾拉", player.Name);
        Assert.Equal(12, player.Level);
        Assert.Equal("0123456789abcdef0123456789abcdef", player.PlayerId);
        Assert.DoesNotContain("10.0.0.8", System.Text.Json.JsonSerializer.Serialize(player));
        Assert.DoesNotContain("hidden", System.Text.Json.JsonSerializer.Serialize(player));
    }

    [Fact]
    public void RosterKeepsBannedPlayersAfterTheyLeave()
    {
        var save = "0123456789abcdef0123456789abcdef";
        var seen = new WorldPlayerView("steam_100", "艾拉", 12, save);
        var rows = WorldPlayers.Merge([seen], [], ["steam_100", "steam_200"], [save, "abcdefabcdefabcdefabcdefabcdefab"]);
        var aila = Assert.Single(rows, row => row.UserId == "steam_100");
        Assert.False(aila.Online);
        Assert.True(aila.Banned);
        Assert.Equal(save, aila.PlayerId);
        var banned = Assert.Single(rows, row => row.UserId == "steam_200");
        Assert.True(banned.Banned);
        Assert.Null(banned.PlayerId);
        Assert.Contains(rows, row => row.UserId == null && row.Banned == null);
        var store = new SqliteStore(Path.Combine(root, "panel.db"));
        store.RememberPlayers("instance", [seen]);
        store.RememberPlayers("instance", [new WorldPlayerView("steam_100", null, null, null)]);
        var remembered = Assert.Single(store.RememberedPlayers("instance"));
        Assert.Equal("艾拉", remembered.Name);
        Assert.Equal(save, remembered.PlayerId);
        store.ForgetInstance("instance");
        Assert.Empty(store.RememberedPlayers("instance"));
    }
}
