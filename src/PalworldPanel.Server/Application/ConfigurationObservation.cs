using System.Text.Json;
using PalworldPanel.Server.Domain;

namespace PalworldPanel.Server.Application;

public static class ConfigurationObservation
{
    public static Dictionary<string, object?> Project(JsonElement settings) => new(StringComparer.Ordinal)
    {
        ["name"] = Value(settings, "ServerName"), ["description"] = Value(settings, "ServerDescription"),
        ["maxPlayers"] = Value(settings, "ServerPlayerMaxNum"), ["deathPenalty"] = Value(settings, "DeathPenalty"),
        ["offlinePenalty"] = Value(settings, "bEnableNonLoginPenalty"),
        ["deteriorationRate"] = Value(settings, "BuildObjectDeteriorationDamageRate"),
        ["attackDamageRate"] = Value(settings, "BuildObjectDamageRate"),
        ["additional"] = GameSettingsFile.Project(settings)
    };
    public static bool Matches(JsonElement settings, GameRules rules)
    {
        var values = Project(settings);
        return Equals(values["name"], rules.Name) && Equals(values["description"], rules.Description) &&
            Number(values["maxPlayers"], rules.MaxPlayers) && Equals(values["deathPenalty"], rules.DeathPenalty) &&
            Equals(values["offlinePenalty"], rules.OfflinePenalty) && Number(values["deteriorationRate"], rules.DeteriorationRate) &&
            Number(values["attackDamageRate"], rules.AttackDamageRate) && GameSettingsFile.Matches(settings, rules.Additional);
    }
    private static bool Number(object? actual, double expected) => actual is double number && Math.Abs(number - expected) < 0.000001;
    private static object? Value(JsonElement settings, string key)
    {
        if (!settings.TryGetProperty(key, out var value)) return null;
        return value.ValueKind switch { JsonValueKind.String => value.GetString(), JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.True => true, JsonValueKind.False => false, _ => null };
    }
}
