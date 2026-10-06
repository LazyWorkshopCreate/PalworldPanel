using PalworldPanel.Server.Domain;

namespace PalworldPanel.Server.Infrastructure;

public static class WorldFiles
{
    public static string Find(string saved, string world)
    {
        if (!Guid.TryParseExact(world, "N", out _)) throw new PanelException("UnknownWorld", "世界身份无效。", 409);
        var root = SafePaths.Within(saved, "SaveGames/0");
        if (!Directory.Exists(root)) throw new PanelException("WorldFilesMissing", "世界未形成持久存档。", 409);
        var matches = Directory.EnumerateDirectories(root).Where(path => string.Equals(Path.GetFileName(path), world, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1) throw new PanelException("WorldPathAmbiguous", "世界目录缺失或存在大小写歧义。", 409);
        SafePaths.RejectLinks(matches[0]);
        return matches[0];
    }
}
