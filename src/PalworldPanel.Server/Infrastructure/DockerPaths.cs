using System.Text.RegularExpressions;

namespace PalworldPanel.Server.Infrastructure;

public static class DockerPaths
{
    // Docker Desktop inspect reports VM paths for Windows bind mounts.
    public static string? WindowsHostPath(string source)
    {
        var value = source.Replace('\\', '/');
        var match = Regex.Match(value, @"^(?:/run/desktop/mnt/host|/host_mnt)/([a-zA-Z])/(.*)$");
        if (match.Success) value = match.Groups[1].Value.ToUpperInvariant() + ":/" + match.Groups[2].Value;
        if (!Regex.IsMatch(value, @"^[a-zA-Z]:/")) return null;
        if (value.Split('/').Any(part => part is ".." or ".") || value.Any(char.IsControl) || value[2..].Contains(':')) return null;
        return value.Replace('/', '\\');
    }
    public static string? LocalPath(string source) => OperatingSystem.IsWindows() ? WindowsHostPath(source) : source;
}
