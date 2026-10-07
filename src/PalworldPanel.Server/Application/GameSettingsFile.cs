using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PalworldPanel.Server.Domain;
using PalworldPanel.Server.Infrastructure;

namespace PalworldPanel.Server.Application;

public static class GameSettingsFile
{
    public const string RelativePath = "data/Pal/Saved/Config/LinuxServer/PalWorldSettings.ini";
    public static Dictionary<string, string> Parse(string source)
    {
        if (source.Length > 1024 * 1024) throw Invalid();
        var match = Regex.Match(source, @"(?m)^\s*OptionSettings\s*=\s*\((.*)\)\s*$");
        if (!match.Success || Regex.Matches(source, @"(?m)^\s*OptionSettings\s*=").Count != 1) throw Invalid();
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var text = match.Groups[1].Value; var start = 0; var depth = 0; var quote = false; var escaped = false;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i == text.Length || text[i] == ',' && depth == 0 && !quote)
            {
                var part = text[start..i].Trim(); start = i + 1;
                if (part.Length == 0) continue;
                var equal = part.IndexOf('=');
                if (equal < 1 || !Regex.IsMatch(part[..equal].Trim(), "^[A-Za-z][A-Za-z0-9_]*$") ||
                    !result.TryAdd(part[..equal].Trim(), part[(equal + 1)..].Trim())) throw Invalid();
                continue;
            }
            var ch = text[i];
            if (quote) { if (escaped) escaped = false; else if (ch == '\\') escaped = true; else if (ch == '"') quote = false; }
            else if (ch == '"') quote = true;
            else if (ch == '(') depth++;
            else if (ch == ')' && --depth < 0) throw Invalid();
        }
        if (quote || depth != 0) throw Invalid();
        return result;
    }
    public static string Decode(string value) => value.StartsWith('"') && value.EndsWith('"')
        ? EnvironmentFile.ReadIniText(value[1..^1]) : value;
    // Unreal rewrites numeric formatting on shutdown. Fingerprint values, preserving unknown settings.
    public static string CanonicalSource(string source)
    {
        var values = Parse(source);
        var normalized = values.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p =>
        {
            var field = GameSettingCatalog.Definitions.FirstOrDefault(d => d.Key.Equals(p.Key, StringComparison.OrdinalIgnoreCase));
            var value = p.Value;
            if (field?.Type == "number" && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number))
                value = number.ToString("G17", CultureInfo.InvariantCulture);
            else if (field?.Type == "boolean" && bool.TryParse(value, out var boolean)) value = boolean ? "True" : "False";
            return new[] { p.Key.ToUpperInvariant(), value };
        });
        // Keep all other sections and directives; only line endings and boundary whitespace are formatting.
        var remainder = Regex.Replace(source, @"(?m)^\s*OptionSettings\s*=\s*\(.*\)\s*$", "")
            .Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        return JsonSerializer.Serialize(new { remainder, values = normalized });
    }
    public static string Patch(string source, IReadOnlyDictionary<string, string> values)
    {
        var existing = Parse(source);
        foreach (var (key, value) in values) existing[key] = value;
        var line = "OptionSettings=(" + string.Join(',', existing.Select(p => p.Key + "=" + p.Value)) + ")";
        return Regex.Replace(source, @"(?m)^\s*OptionSettings\s*=\s*\(.*\)\s*$", _ => line);
    }
    public static Dictionary<string, string> Read(InstanceRecord instance)
    {
        var file = SafePaths.Within(instance.Root, RelativePath);
        if (!File.Exists(file)) return new(StringComparer.Ordinal);
        return Parse(File.ReadAllText(file)).Where(p => GameSettingCatalog.Definitions.Any(d => d.Key == p.Key && !d.Secret && d.RuleKey is null))
            .ToDictionary(p => p.Key, p => Decode(p.Value), StringComparer.Ordinal);
    }
    public static void Write(InstanceRecord instance, SecretVault vault)
    {
        var path = SafePaths.Within(instance.Root, RelativePath);
        var source = File.Exists(path) ? File.ReadAllText(path) : "[/Script/Pal.PalGameWorldSettings]\nOptionSettings=()\n";
        var existing = Parse(source);
        var changes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in GameSettingCatalog.Definitions.Where(d => d.DefaultValue is not null && !d.Secret && d.RuleKey is null))
            if (!existing.ContainsKey(field.Key)) changes[field.Key] = GameSettingCatalog.Encode(field, field.DefaultValue!);
        foreach (var (key, value) in instance.Desired.Additional ?? [])
            changes[key] = GameSettingCatalog.Encode(GameSettingCatalog.Definitions.Single(d => d.Key == key), value);
        var rules = instance.Desired;
        changes["ServerName"] = GameSettingCatalog.Quote(rules.Name);
        changes["ServerDescription"] = GameSettingCatalog.Quote(rules.Description);
        changes["ServerPlayerMaxNum"] = rules.MaxPlayers.ToString(CultureInfo.InvariantCulture);
        changes["DeathPenalty"] = rules.DeathPenalty;
        changes["bEnableNonLoginPenalty"] = rules.OfflinePenalty ? "True" : "False";
        changes["BuildObjectDeteriorationDamageRate"] = rules.DeteriorationRate.ToString(CultureInfo.InvariantCulture);
        changes["BuildObjectDamageRate"] = rules.AttackDamageRate.ToString(CultureInfo.InvariantCulture);
        changes["RESTAPIEnabled"] = "True"; changes["RESTAPIPort"] = "8212";
        changes["RCONEnabled"] = "False"; changes["RCONPort"] = "25575";
        changes["AdminPassword"] = GameSettingCatalog.Quote(vault.Open(instance.AdminCipher, instance.Id + ":admin"));
        changes["ServerPassword"] = GameSettingCatalog.Quote(vault.Open(instance.GameCipher, instance.Id + ":game"));
        DurableFile.Write(path, Encoding.UTF8.GetBytes(Patch(source, changes)));
        TreeFiles.PrepareGameOwnership(Path.GetDirectoryName(path)!);
    }
    public static Dictionary<string, string> Project(JsonElement settings) => settings.EnumerateObject()
        .Where(p => GameSettingCatalog.Definitions.Any(d => d.Key == p.Name && !d.Secret && d.RuleKey is null))
        .Where(p => p.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Object))
        .ToDictionary(p => p.Name, p => p.Value.ValueKind == JsonValueKind.Array ? "(" + string.Join(',', p.Value.EnumerateArray().Select(v => v.ToString())) + ")" : p.Value.ToString(), StringComparer.Ordinal);
    public static bool Matches(JsonElement settings, Dictionary<string, string>? desired) => desired is null || desired.All(pair =>
        !settings.TryGetProperty(pair.Key, out var actual) ||
        GameSettingCatalog.Encode(GameSettingCatalog.Definitions.Single(d => d.Key == pair.Key), ProjectValue(actual)) ==
        GameSettingCatalog.Encode(GameSettingCatalog.Definitions.Single(d => d.Key == pair.Key), pair.Value));
    private static string ProjectValue(JsonElement value) => value.ValueKind == JsonValueKind.Array
        ? "(" + string.Join(',', value.EnumerateArray().Select(v => v.ToString())) + ")" : value.ToString();
    private static PanelException Invalid() => new("UnsupportedConfig", "世界配置格式或重复字段无法安全解析。", 409);
}
