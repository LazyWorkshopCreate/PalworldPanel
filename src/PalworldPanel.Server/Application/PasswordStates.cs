using PalworldPanel.Server.Domain;
using PalworldPanel.Server.Infrastructure;

namespace PalworldPanel.Server.Application;

public sealed record PasswordState(bool? CurrentConfigured, bool Modified);
public sealed record InstancePasswordStates(PasswordState Administrator, PasswordState Game);

public static class PasswordStates
{
    public static InstancePasswordStates Project(InstanceRecord instance, SecretVault vault) => new(
        State(instance.AdminCipher, instance.AppliedAdminCipher, instance.Id + ":admin", instance.Applied is not null, vault),
        State(instance.GameCipher, instance.AppliedGameCipher, instance.Id + ":game", instance.Applied is not null, vault));

    private static PasswordState State(string draft, string? applied, string purpose, bool known, SecretVault vault)
    {
        if (!known) return new(null, true);
        var baseline = applied ?? draft;
        // Ciphertexts are randomized; compare values internally and expose only state.
        var changed = draft != baseline && !string.Equals(vault.Open(draft, purpose), vault.Open(baseline, purpose), StringComparison.Ordinal);
        return new(SecretVault.IsConfigured(baseline), changed);
    }
}
