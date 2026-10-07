using PalworldPanel.Server.Domain;

namespace PalworldPanel.UnitTests;

public sealed class InstanceActionPolicyTests
{
    private static readonly GameRules Rules = new("synthetic");
    private static readonly InstanceRecord Instance = new("synthetic", "synthetic", "/synthetic", "synthetic", "palworld", "synthetic", 19211, 19312, 19415,
        "cipher", "cipher", Rules, Rules, Writable: true, Owned: true);

    [Theory]
    [InlineData("start", "running", false)]
    [InlineData("start", "stopped", true)]
    [InlineData("stop", "stopped", false)]
    [InlineData("stop", "running", true)]
    [InlineData("save", "exited", false)]
    [InlineData("save", "running", true)]
    [InlineData("restart", "created", false)]
    [InlineData("restart", "running", true)]
    [InlineData("force-stop", "stopped", false)]
    [InlineData("force-stop", "paused", true)]
    [InlineData("backup", "exited", true)]
    [InlineData("backup", "running", true)]
    [InlineData("upgrade", "restarting", false)]
    [InlineData("retain-data", "removing", false)]
    public void RuntimeStateControlsOperations(string action, string state, bool enabled) =>
        Assert.Equal(enabled, InstanceActionPolicy.DisabledReason(Instance, action, state, "healthy") is null);

    [Fact]
    public void EveryToolbarActionHasDescriptionAndIsBlockedByOutstandingTask()
    {
        Assert.Equal(14, InstanceActionPolicy.Rules.Count);
        foreach (var (action, rule) in InstanceActionPolicy.Rules)
        {
            Assert.False(string.IsNullOrWhiteSpace(rule.Description));
            Assert.NotNull(InstanceActionPolicy.DisabledReason(Instance, action, "running", "healthy", locked: true));
        }
    }

    [Fact]
    public void UnverifiedStateAndUnavailableApiCannotTriggerSafeMutation()
    {
        foreach (var action in new[] { "start", "stop", "restart", "save", "backup", "upgrade", "retain-data", "purge" })
            Assert.NotNull(InstanceActionPolicy.DisabledReason(Instance, action, "running", "healthy", stale: true));
        foreach (var action in new[] { "stop", "restart", "save", "backup", "upgrade", "retain-data", "purge" })
            Assert.NotNull(InstanceActionPolicy.DisabledReason(Instance, action, "running", "unauthorized"));
        Assert.Null(InstanceActionPolicy.DisabledReason(Instance, "force-stop", "running", "unreachable"));
    }

    [Fact]
    public void ReadOnlyAndQuarantineHaveSpecificAllowedActions()
    {
        var readOnly = Instance with { Writable = false, Owned = false, SourceHash = "verified" };
        Assert.Null(InstanceActionPolicy.DisabledReason(readOnly, "clone", "unknown", "unknown"));
        Assert.Null(InstanceActionPolicy.DisabledReason(readOnly, "adopt", "stopped", "unknown"));
        Assert.NotNull(InstanceActionPolicy.DisabledReason(readOnly, "start", "stopped", "unknown"));
        Assert.NotNull(InstanceActionPolicy.DisabledReason(readOnly with { Writable = true }, "purge", "stopped", "unknown"));
        var now = DateTimeOffset.Parse("2026-10-07T12:00:00+08:00");
        var isolated = Instance with { Writable = false, QuarantinedUtc = now.AddDays(-6).ToString("O") };
        Assert.Null(InstanceActionPolicy.DisabledReason(isolated, "undo-quarantine", "unknown", "unknown"));
        Assert.NotNull(InstanceActionPolicy.DisabledReason(isolated, "finalize-purge", "unknown", "unknown", now: now));
        Assert.Null(InstanceActionPolicy.DisabledReason(isolated with { QuarantinedUtc = now.AddDays(-7).ToString("O") }, "finalize-purge", "unknown", "unknown", now: now));
        Assert.NotNull(InstanceActionPolicy.DisabledReason(isolated, "clone", "unknown", "unknown"));
        Assert.NotNull(InstanceActionPolicy.DisabledReason(isolated, "unmanage", "unknown", "unknown"));
        Assert.NotNull(InstanceActionPolicy.DisabledReason(isolated with { PurgedUtc = now.ToString("O") }, "undo-quarantine", "unknown", "unknown"));
    }

    [Fact]
    public void UnappliedRulesOrSecretsBlockUpgrade()
    {
        Assert.NotNull(InstanceActionPolicy.DisabledReason(Instance with { Desired = Rules with { MaxPlayers = 20 } }, "upgrade", "stopped", "unknown"));
        Assert.NotNull(InstanceActionPolicy.DisabledReason(Instance with { AppliedAdminCipher = "old" }, "upgrade", "stopped", "unknown"));
        Assert.NotNull(InstanceActionPolicy.DisabledReason(Instance with { AppliedGameCipher = "old" }, "upgrade", "stopped", "unknown"));
    }

    [Fact]
    public void AbsentContainerDisablesRedundantRemovalButAllowsStartAndBackup()
    {
        Assert.NotNull(InstanceActionPolicy.DisabledReason(Instance, "retain-data", "stopped", "unknown", containerPresent: false));
        Assert.Null(InstanceActionPolicy.DisabledReason(Instance, "start", "stopped", "unknown", containerPresent: false));
        Assert.Null(InstanceActionPolicy.DisabledReason(Instance, "backup", "stopped", "unknown", containerPresent: false));
    }
}
