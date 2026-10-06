using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using PalworldPanel.Server.Infrastructure;

namespace PalworldPanel.Server.Domain;

public sealed record GameSettingDefinition(string Key, string Label, string Type, string? DefaultValue,
    string? RuleKey = null, bool Editable = true, bool Secret = false, bool Integer = false);

public static class GameSettingCatalog
{
    public static readonly GameSettingDefinition[] Definitions = Load();
    private static GameSettingDefinition[] Load()
    {
        using var stream = typeof(GameSettingCatalog).Assembly.GetManifestResourceStream("game-settings.json")!;
        return JsonSerializer.Deserialize<GameSettingDefinition[]>(stream, DurableFile.Json)!;
    }
    public static readonly Dictionary<string, string[]> Choices = new(StringComparer.Ordinal)
    {
        ["Difficulty"] = ["None", "Easy", "Normal", "Hard"],
        ["RandomizerType"] = ["None", "Region", "All"],
        ["LogFormatType"] = ["Text", "Json"]
    };
    public static void Validate(Dictionary<string, string>? values)
    {
        if (values is null) return;
        if (values.Count > Definitions.Length) throw Invalid();
        foreach (var (key, value) in values)
        {
            var field = Definitions.SingleOrDefault(d => d.Key == key);
            if (field is null || !field.Editable || field.Secret || field.RuleKey is not null || value is null ||
                value.Length > 4096 || value.Any(char.IsControl)) throw Invalid();
            _ = Encode(field, value);
        }
    }
    public static string Encode(GameSettingDefinition field, string value)
    {
        if (value.Any(char.IsControl) || value.Length > 4096) throw Invalid();
        if (field.Type == "boolean")
            return bool.TryParse(value, out var flag) ? flag ? "True" : "False" : throw Invalid();
        if (field.Type == "number")
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
                !double.IsFinite(number) || number < (field.Key == "PhysicsActiveDropItemMaxNum" ? -1 : 0) || number > 1e12)
                throw Invalid();
            if (field.Key is "BaseCampMaxNumInGuild" && (number < 1 || number > 10) ||
                field.Key is "BaseCampWorkerMaxNum" && (number < 1 || number > 50) ||
                field.Key is "ServerReplicatePawnCullDistance" && (number < 5000 || number > 15000) ||
                field.Key is "FishingDifficultyRate" && (number < 0.1 || number > 1)) throw Invalid();
            if (field.Integer && (number != Math.Truncate(number) || number > int.MaxValue)) throw Invalid();
            return number.ToString("G17", CultureInfo.InvariantCulture);
        }
        if (field.Type == "enum")
            return Choices.TryGetValue(field.Key, out var choices) && choices.Contains(value, StringComparer.Ordinal) ? value : throw Invalid();
        if (field.Type == "list")
        {
            var contents = value.Trim().Trim('(', ')');
            // This game field uses a blank assignment for an empty list; () becomes NAME_None.
            if (string.IsNullOrWhiteSpace(contents)) return field.Key == "DenyTechnologyList" ? "" : "()";
            var items = contents.Split(',').Select(v => v.Trim().Trim('"')).ToArray();
            if (items.Any(v => !Regex.IsMatch(v, "^[A-Za-z0-9_-]{1,128}$")) || items.Length > 256) throw Invalid();
            if (field.Key == "CrossplayPlatforms" && items.Any(v => v is not ("Steam" or "Xbox" or "PS5" or "Mac"))) throw Invalid();
            return "(" + string.Join(',', items.Select(v => field.Key == "CrossplayPlatforms" ? v : Quote(v))) + ")";
        }
        if (field.Key == "BanListURL" && value.Length > 0 && (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0)) throw Invalid();
        return Quote(value);
    }
    public static string Quote(string value) => "\"" + EnvironmentFile.IniText(value) + "\"";
    private static PanelException Invalid() => new("InvalidSetting", "参数字段、类型或取值不合法。", 400);
}
