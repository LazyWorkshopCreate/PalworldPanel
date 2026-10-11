namespace PalworldPanel.Server.Infrastructure;

public sealed record SaveFileEntry(string Name, string Role, long Bytes, string Container, string? Reason);
public sealed record SaveInventoryResult(SaveFileEntry[] Files, bool PlayersTruncated);

public static class SaveFiles
{
    public const int MaxPlayers = 200;

    public static SaveInventoryResult Read(string worldDirectory)
    {
        SafePaths.RejectLinks(worldDirectory);
        var files = new List<SaveFileEntry>();
        foreach (var name in new[] { "Level.sav", "LevelMeta.sav", "WorldOption.sav", "LocalData.sav" })
        {
            var path = Path.Combine(worldDirectory, name);
            if (File.Exists(path)) files.Add(Describe(path, name, "world"));
        }
        var players = Path.Combine(worldDirectory, "Players");
        var truncated = false;
        if (Directory.Exists(players))
        {
            SafePaths.RejectLinks(players);
            var matches = PlayerNames(players);
            truncated = matches.Length > MaxPlayers;
            foreach (var name in matches.Take(MaxPlayers))
                files.Add(Describe(Path.Combine(players, name), "Players/" + name, "player"));
        }
        return new(files.ToArray(), truncated);
    }

    public static string[] PlayerIds(string worldDirectory)
    {
        SafePaths.RejectLinks(worldDirectory);
        var players = Path.Combine(worldDirectory, "Players");
        if (!Directory.Exists(players)) return [];
        SafePaths.RejectLinks(players);
        return PlayerNames(players).Take(MaxPlayers).Select(name => Path.GetFileNameWithoutExtension(name)).ToArray();
    }

    private static string[] PlayerNames(string players) => Directory.EnumerateFiles(players, "*.sav")
        .Select(Path.GetFileName).Where(name => name is not null && PlayerFile(name))
        .Order(StringComparer.OrdinalIgnoreCase).Cast<string>().ToArray();

    private static SaveFileEntry Describe(string path, string name, string role)
    {
        SafePaths.RejectLinks(path);
        var length = new FileInfo(path).Length;
        byte[] sample;
        if (length <= SaveContainer.MaxBodyBytes)
            sample = File.ReadAllBytes(path);
        else
        {
            sample = new byte[24];
            using var stream = File.OpenRead(path);
            stream.ReadExactly(sample);
        }
        var info = SaveContainer.Describe(sample, length);
        var reason = info.Reason;
        if (role == "player" && length > SaveContainer.MaxBodyBytes) reason = "玩家存档超过 32 MiB。";
        return new(name, role, length, info.Kind, reason);
    }

    private static bool PlayerFile(string name) => Guid.TryParseExact(Path.GetFileNameWithoutExtension(name), "N", out _);
}
