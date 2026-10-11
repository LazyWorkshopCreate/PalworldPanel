using System.Text;
using System.Text.RegularExpressions;
using PalworldPanel.Server.Domain;

namespace PalworldPanel.Server.Infrastructure;

public static partial class ServerBanList
{
    public const int MaxEntries = 500;

    public static bool IsUserId(string? value) => value is not null && UserIdPattern().IsMatch(value);

    public static IReadOnlyList<string> Read(string saveGamesDirectory)
    {
        var path = Path.Combine(saveGamesDirectory, "banlist.txt");
        if (!File.Exists(path)) return [];
        SafePaths.RejectLinks(path);
        return Parse(File.ReadAllText(path));
    }

    public static void Write(string saveGamesDirectory, IReadOnlyList<string> ids)
    {
        if (!Directory.Exists(saveGamesDirectory)) throw new PanelException("WorldFilesMissing", "世界目录尚未形成，不能写入封禁列表。", 409);
        SafePaths.RejectLinks(saveGamesDirectory);
        var distinct = ids.Distinct(StringComparer.Ordinal).ToArray();
        if (distinct.Length > MaxEntries || distinct.Any(id => !IsUserId(id)))
            throw new PanelException("InvalidPlayer", "封禁列表包含无效玩家编号。", 400);
        var path = SafePaths.Within(saveGamesDirectory, "banlist.txt");
        var text = distinct.Length == 0 ? "" : string.Join('\n', distinct) + "\n";
        DurableFile.Write(path, Encoding.UTF8.GetBytes(text));
    }

    public static IReadOnlyList<string> Parse(string text)
    {
        var result = new List<string>();
        foreach (var raw in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (!IsUserId(line) || result.Contains(line, StringComparer.Ordinal))
                throw new PanelException("InvalidPlayer", "封禁列表无法安全读取。", 409);
            if (result.Count == MaxEntries) throw new PanelException("InvalidPlayer", "封禁列表超过上限。", 409);
            result.Add(line);
        }
        return result;
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,63}$")]
    private static partial Regex UserIdPattern();
}
