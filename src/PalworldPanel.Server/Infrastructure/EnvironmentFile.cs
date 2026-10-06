using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PalworldPanel.Server.Domain;

namespace PalworldPanel.Server.Infrastructure;

public static partial class EnvironmentFile
{
    // The approved image adds the surrounding INI quotes; escape only their contents.
    public static string IniText(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    public static string ReadIniText(string value) => Regex.Replace(value, "\\\\([\\\\\"])", "$1");
    public static string Patch(string source, IReadOnlyDictionary<string, string> changes)
    {
        var remaining = new Dictionary<string, string>(changes, StringComparer.Ordinal);
        foreach (var pair in changes)
        {
            if (!Key().IsMatch(pair.Key) || pair.Value.Any(char.IsControl))
                throw new PanelException("InvalidSetting", "配置键或值格式无效。");
        }
        var newline = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = source.Split(newline);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < lines.Length; i++)
        {
            var match = Assignment().Match(lines[i]);
            if (!match.Success) continue;
            var key = match.Groups[2].Value;
            if (!keys.Add(key)) throw new PanelException("UnsupportedConfig", "配置中存在重复键，保持只读。");
            if (remaining.Remove(key, out var value)) lines[i] = match.Groups[1].Value + key + "=" + Quote(value);
        }
        var result = string.Join(newline, lines).TrimEnd('\r', '\n');
        foreach (var pair in remaining) result += newline + pair.Key + "=" + Quote(pair.Value);
        return result + newline;
    }

    public static string Quote(string value)
    {
        if (value.Any(char.IsControl)) throw new PanelException("InvalidSetting", "配置值不能包含控制字符。");
        // Compose env_file format: raw treats every character literally, including dollars.
        return value;
    }

    public static Dictionary<string, string> ParseRaw(string source)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in source.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            var match = Assignment().Match(trimmed);
            if (!match.Success)
            {
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.TrimStart().StartsWith('#')) continue;
                throw new PanelException("UnsupportedConfig", "不支持的配置行，保持只读。");
            }
            var key = match.Groups[2].Value;
            var value = trimmed[(trimmed.IndexOf('=') + 1)..];
            if (!result.TryAdd(key, value)) throw new PanelException("UnsupportedConfig", "配置键重复。");
        }
        return result;
    }

    public static byte[] SerializeRaw(IReadOnlyDictionary<string, string> values) => Encoding.UTF8.GetBytes(
        string.Join('\n', values.Select(pair => pair.Key + "=" + Quote(pair.Value))) + "\n");

    [GeneratedRegex("^[A-Z][A-Z0-9_]*$")] private static partial Regex Key();
    [GeneratedRegex("^(\\s*)([A-Z][A-Z0-9_]*)\\s*=")] private static partial Regex Assignment();
}
