using System.Security.Cryptography;
using System.Text.Json;
using PalworldPanel.Server.Application;
using PalworldPanel.Server.Domain;
using PalworldPanel.Server.Infrastructure;
using Xunit;

namespace PalworldPanel.UnitTests;

public sealed class PasswordStatesTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "pp-password-states-" + Guid.NewGuid().ToString("N"));
    private readonly SecretVault vault;
    public PasswordStatesTests()
    {
        Directory.CreateDirectory(root);
        var key = Path.Combine(root, "key");
        File.WriteAllBytes(key, RandomNumberGenerator.GetBytes(32));
        vault = new SecretVault(key);
    }
    private InstanceRecord Instance()
    {
        var rules = new GameRules("synthetic");
        return new("synthetic", "synthetic", root, "project", "palworld", "image", 18211, 18312, 18415,
            vault.Seal("synthetic-admin", "synthetic:admin"), vault.Seal("synthetic-game", "synthetic:game"), rules, rules);
    }
    [Fact]
    public void SamePasswordWithDifferentNoncesIsNotModifiedAndNeverReturned()
    {
        var instance = Instance();
        instance = instance with { AppliedAdminCipher = vault.Seal("synthetic-admin", "synthetic:admin") };
        var state = PasswordStates.Project(instance, vault);
        Assert.True(state.Administrator.CurrentConfigured);
        Assert.False(state.Administrator.Modified);
        Assert.False(state.Game.Modified);
        var json = JsonSerializer.Serialize(state);
        Assert.DoesNotContain("synthetic-admin", json);
        Assert.DoesNotContain(instance.AdminCipher, json);
        Assert.DoesNotContain(instance.AppliedAdminCipher, json);
    }
    [Fact]
    public void ClearedDraftIsModifiedUntilAppliedThenCurrentIsUnset()
    {
        var instance = Instance();
        instance = instance with { AppliedGameCipher = instance.GameCipher, GameCipher = vault.Seal("", "synthetic:game") };
        var state = PasswordStates.Project(instance, vault);
        Assert.True(state.Game.CurrentConfigured);
        Assert.True(state.Game.Modified);
        instance = instance with { AppliedGameCipher = instance.GameCipher };
        state = PasswordStates.Project(instance, vault);
        Assert.False(state.Game.CurrentConfigured);
        Assert.False(state.Game.Modified);
    }
    [Fact]
    public void ChangedAdminAndFirstApplyAreReportedWithoutInventingCurrentValue()
    {
        var instance = Instance();
        instance = instance with { AppliedAdminCipher = instance.AdminCipher, AdminCipher = vault.Seal("synthetic-new-admin", "synthetic:admin") };
        Assert.True(PasswordStates.Project(instance, vault).Administrator.Modified);
        var state = PasswordStates.Project(instance with { Applied = null }, vault);
        Assert.Null(state.Administrator.CurrentConfigured);
        Assert.True(state.Administrator.Modified);
    }
    public void Dispose() => Directory.Delete(root, true);
}
