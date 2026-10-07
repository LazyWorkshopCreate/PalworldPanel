using PalworldPanel.Server.Application;
using PalworldPanel.Server.Domain;
using PalworldPanel.Server.Infrastructure;
using System.Security.Cryptography;
using Xunit;

namespace PalworldPanel.UnitTests;

public sealed class SourceFingerprintTests
{
    [Fact]
    public void ShutdownFormattingDoesNotChangeFingerprint()
    {
        var before = "[/Script/Pal.PalGameWorldSettings]\r\nOptionSettings=(ExpRate=2,bEnableNonLoginPenalty=True,ServerName=\"world\",FutureSetting=opaque)\r\n";
        var after = "[/Script/Pal.PalGameWorldSettings]\nOptionSettings=(ServerName=\"world\",FutureSetting=opaque,bEnableNonLoginPenalty=true,ExpRate=2.000000)\n";
        Assert.Equal(GameSettingsFile.CanonicalSource(before), GameSettingsFile.CanonicalSource(after));
    }

    [Theory]
    [InlineData("ExpRate=3,AdminPassword=\"secret\",FutureSetting=opaque")]
    [InlineData("ExpRate=2,AdminPassword=\"changed\",FutureSetting=opaque")]
    [InlineData("ExpRate=2,AdminPassword=\"secret\",FutureSetting=changed")]
    [InlineData("ExpRate=2,AdminPassword=\"secret\"")]
    public void RealChangesRemainDetectable(string changed)
    {
        var before = "OptionSettings=(ExpRate=2,AdminPassword=\"secret\",FutureSetting=opaque)";
        Assert.NotEqual(GameSettingsFile.CanonicalSource(before), GameSettingsFile.CanonicalSource("OptionSettings=(" + changed + ")"));
    }

    [Fact]
    public void OtherSectionsRemainDetectableAndDuplicatesAreRejected()
    {
        Assert.NotEqual(GameSettingsFile.CanonicalSource("[Engine]\nValue=1\nOptionSettings=()"),
            GameSettingsFile.CanonicalSource("[Engine]\nValue=2\nOptionSettings=()"));
        Assert.Throws<PanelException>(() => GameSettingsFile.CanonicalSource("OptionSettings=(ExpRate=2,exprate=3)"));
    }

    [Fact]
    public void LegacyMigrationOnlyAcceptsUnchangedIdleSources()
    {
        var root = Path.Combine(Path.GetTempPath(), "pp-fingerprint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new SqliteStore(Path.Combine(root, "state", "panel.db"));
            var options = new PanelOptions("127.0.0.1", 18080, [], Path.Combine(root, "state"), [root], Path.Combine(root, "backups"), "", "", "", "", "image", ["image"]);
            var service = new InstanceService(options, store, null!, null!, null!, null!);
            InstanceRecord Make(string name)
            {
                var id = Guid.NewGuid().ToString("N"); var path = Path.Combine(root, id); Directory.CreateDirectory(path);
                foreach (var file in new[] { "compose.yaml", "settings.env", "secrets.env" }) File.WriteAllText(Path.Combine(path, file), "");
                var hash = Convert.ToHexString(SHA256.HashData(new byte[3]));
                return new InstanceRecord(id, name, path, "pp-" + id, "palworld", "image", 18211, 18312, 18415, "", "", new GameRules(name), SourceHash: hash);
            }
            var idle = Make("idle"); var drift = Make("drift"); var busy = Make("busy");
            foreach (var instance in new[] { idle, drift, busy }) store.SaveInstance(instance);
            File.WriteAllText(Path.Combine(drift.Root, "settings.env"), "CHANGED=true");
            store.Enqueue(busy.Id, "start", "{}", "admin", Guid.NewGuid().ToString(), "hash");
            service.MigrateSourceHashes();
            Assert.StartsWith("v2:", store.Instance(idle.Id).SourceHash);
            Assert.Equal(idle.Revision, store.Instance(idle.Id).Revision);
            Assert.Equal(drift.SourceHash, store.Instance(drift.Id).SourceHash);
            Assert.Equal(busy.SourceHash, store.Instance(busy.Id).SourceHash);
            Assert.Throws<PanelException>(() => service.CheckSource(drift));
        }
        finally
        {
            using var connection = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
                { DataSource = Path.Combine(root, "state", "panel.db"), ForeignKeys = true, Pooling = true }.ToString());
            Microsoft.Data.Sqlite.SqliteConnection.ClearPool(connection);
            Directory.Delete(root, true);
        }
    }
}
