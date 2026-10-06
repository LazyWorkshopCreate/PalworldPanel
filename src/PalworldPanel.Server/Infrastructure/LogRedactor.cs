using System.Text.RegularExpressions;

namespace PalworldPanel.Server.Infrastructure;

public static partial class LogRedactor
{
    public static string LatestFirst(string value)
    {
        var records = new List<(string Timestamp, List<string> Lines)>();
        foreach (var line in value.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n').Split('\n'))
        {
            var timestamp = LogTimestamp().Match(line);
            if (timestamp.Success || records.Count == 0) records.Add((timestamp.Value, [line]));
            else records[^1].Lines.Add(line);
        }
        return string.Join('\n', records.OrderByDescending(record => record.Timestamp, StringComparer.Ordinal)
            .SelectMany(record => record.Lines));
    }

    [GeneratedRegex("^\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}\\.\\d{9}Z")]
    private static partial Regex LogTimestamp();

    public static string Clean(string value, params string[] knownSecrets)
    {
        foreach (var secret in knownSecrets.Where(s => !string.IsNullOrEmpty(s)).SelectMany(secret => new[]
            { secret, EnvironmentFile.IniText(secret), Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("admin:" + secret)) }).Distinct(StringComparer.Ordinal).OrderByDescending(s => s.Length))
            value = value.Replace(secret, "[redacted]", StringComparison.Ordinal);
        value = SensitiveLine().Replace(value, "[redacted sensitive field]");
        value = UserInfo().Replace(value, "$1[redacted]@");
        value = Ipv4().Replace(value, "[redacted IP]");
        value = Ipv6().Replace(value, match => System.Net.IPAddress.TryParse(match.Value, out var address) &&
            address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? "[redacted IP]" : match.Value);
        value = PlatformId().Replace(value, "[redacted ID]");
        return string.Concat(value.Select(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t') ? ' ' : c));
    }
    [GeneratedRegex("^.*(?:PASSWORD|authorization|token|secret|userid|playeruid|playerip|playername|steamid|epicid|player.*(?:joined|connected)).*$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex SensitiveLine();
    [GeneratedRegex("(https?://)[^/\\s]+@", RegexOptions.IgnoreCase)] private static partial Regex UserInfo();
    [GeneratedRegex("\\b(?:\\d{1,3}\\.){3}\\d{1,3}\\b")] private static partial Regex Ipv4();
    [GeneratedRegex("\\b[0-9]{16,20}\\b")] private static partial Regex PlatformId();
    [GeneratedRegex("(?<![A-Za-z0-9:])(?:[A-Fa-f0-9]{0,4}:){2,}[A-Fa-f0-9:.]*(?![A-Za-z0-9:])")] private static partial Regex Ipv6();
}
