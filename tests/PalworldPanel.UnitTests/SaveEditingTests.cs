using System.Security.Cryptography;
using PalworldPanel.Server.Domain;
using PalworldPanel.Server.Infrastructure;

namespace PalworldPanel.UnitTests;

public sealed class SaveEditingTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "palworldpanel-saves-" + Guid.NewGuid().ToString("N"));
    public SaveEditingTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);

    [Theory]
    [InlineData(0x31)]
    [InlineData(0x32)]
    public void ZlibSaveRoundTripsAndDoesNotBecomeWritable(int saveType)
    {
        var body = "GVAS-synthetic-body"u8.ToArray();
        var file = SaveContainer.EncodePlZ(body, (byte)saveType);
        Assert.Equal(body, SaveContainer.DecodePlZ(file));
        var info = SaveContainer.Describe(file, file.Length);
        Assert.Equal("PlZ", info.Kind);
        Assert.True(info.Gvas);
        Assert.False(info.ContentWritable);
        Assert.Null(info.Reason);
    }

    [Fact]
    public void CurrentCompressionAndBrokenLengthsStayClosed()
    {
        var file = SaveContainer.EncodePlZ("GVAS-synthetic"u8.ToArray(), 0x31);
        file[10] = (byte)'M';
        var oodle = SaveContainer.Describe(file, file.Length);
        Assert.Equal("PlM", oodle.Kind);
        Assert.False(oodle.Gvas);
        Assert.Contains("不能由面板解码", oodle.Reason);
        file[10] = (byte)'Z';
        file[0] = 9;
        Assert.Contains("长度", SaveContainer.Describe(file, file.Length).Reason);
        Assert.Equal("UnsupportedSave", Assert.Throws<PanelException>(() => SaveContainer.DecodePlZ(new byte[8])).Code);
    }

    [Fact]
    public async Task InventoryListsPlayerFileAndLeavesItInPlace()
    {
        var world = Path.Combine(root, "world");
        var players = Path.Combine(world, "Players");
        Directory.CreateDirectory(players);
        var level = SaveContainer.EncodePlZ("GVAS-world"u8.ToArray(), 0x31);
        File.WriteAllBytes(Path.Combine(world, "Level.sav"), level);
        var playerId = Guid.NewGuid().ToString("N");
        File.WriteAllBytes(Path.Combine(players, playerId.ToUpperInvariant() + ".sav"), SaveContainer.EncodePlZ("GVAS-player"u8.ToArray(), 0x32));
        var inventory = SaveFiles.Read(world);
        Assert.Contains(inventory.Files, file => file.Name == "Level.sav" && file.Role == "world");
        Assert.Contains(inventory.Files, file => file.Name == "Players/" + playerId.ToUpperInvariant() + ".sav" && file.Role == "player");
        Assert.DoesNotContain(":", string.Join('\n', inventory.Files.Select(file => file.Name)));
        Assert.True(File.Exists(Path.Combine(players, playerId.ToUpperInvariant() + ".sav")));
        Assert.Equal(SHA256.HashData(level), SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(world, "Level.sav"))));
    }
}
