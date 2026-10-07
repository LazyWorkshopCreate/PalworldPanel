using System.Text.RegularExpressions;
using PalworldPanel.Server.Domain;

namespace PalworldPanel.Server.Infrastructure;

public sealed record SteamInstallation(string BuildId, string DepotManifest)
{
    public static SteamInstallation Read(InstanceRecord instance)
    {
        var path = SafePaths.Within(instance.Root, "data/steamapps/appmanifest_2394010.acf");
        if (!File.Exists(path) || new FileInfo(path).Length > 1024 * 1024)
            throw new PanelException("InstallationUnknown", "无法核实此实例的安装版本，暂不能升级。", 409);
        return Parse(File.ReadAllText(path));
    }

    public static SteamInstallation Parse(string text)
    {
        try
        {
            var tokens = Regex.Matches(text, "\"(?:[^\"\\\\]|\\\\.)*\"|[{}]").Select(match => match.Value).ToArray();
            var index = 0;
            Dictionary<string, object> Object(int depth)
            {
                if (depth > 16) throw new FormatException();
                var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                while (index < tokens.Length && tokens[index] != "}")
                {
                    var key = Value(tokens[index++]);
                    if (index >= tokens.Length) throw new FormatException();
                    object value;
                    if (tokens[index] == "{")
                    {
                        index++;
                        value = Object(depth + 1);
                        if (index >= tokens.Length || tokens[index++] != "}") throw new FormatException();
                    }
                    else value = Value(tokens[index++]);
                    result.Add(key, value);
                }
                return result;
            }
            var root = Object(0);
            if (index != tokens.Length) throw new FormatException();
            var state = (Dictionary<string, object>)root["AppState"];
            if ((string)state["appid"] != "2394010" || (string)state["StateFlags"] != "4" ||
                (state.TryGetValue("UpdateResult", out var update) && (string)update != "0")) throw new FormatException();
            if (state.TryGetValue("UserConfig", out var config) && ((Dictionary<string, object>)config).TryGetValue("BetaKey", out var beta) && !string.IsNullOrEmpty((string)beta)) throw new FormatException();
            var depots = (Dictionary<string, object>)state["InstalledDepots"];
            var linux = (Dictionary<string, object>)depots["2394012"];
            var build = (string)state["buildid"];
            var manifest = (string)linux["manifest"];
            if (!SteamReleaseCatalog.IsNumber(build) || !SteamReleaseCatalog.IsNumber(manifest)) throw new FormatException();
            return new(build, manifest);
        }
        catch (Exception error) when (error is FormatException or KeyNotFoundException or InvalidCastException or ArgumentException)
        { throw new PanelException("InstallationUnknown", "安装信息不完整或不属于正式 Linux 版本，暂不能升级。", 409); }
    }

    private static string Value(string token) => token.StartsWith('"') && token.EndsWith('"')
        ? token[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal)
        : throw new FormatException();
}
