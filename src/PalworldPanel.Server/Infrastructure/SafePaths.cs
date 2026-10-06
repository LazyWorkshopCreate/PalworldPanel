using PalworldPanel.Server.Domain;

namespace PalworldPanel.Server.Infrastructure;

public static class SafePaths
{
    private static readonly StringComparison Comparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static string Within(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) ||
            relative.Contains(':') || relative.Contains('\0') ||
            relative.Replace('\\', '/').Split('/').Any(part => part is ".." or "."))
            throw new PanelException("UnsafePath", "文件路径不安全。");
        var canonicalRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var path = Path.GetFullPath(Path.Combine(canonicalRoot, relative));
        if (!path.StartsWith(canonicalRoot + Path.DirectorySeparatorChar, Comparison))
            throw new PanelException("UnsafePath", "文件必须位于批准目录内。");
        RejectLinks(path);
        return path;
    }

    public static void RejectLinks(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            var info = Directory.Exists(current) ? (FileSystemInfo)new DirectoryInfo(current) : new FileInfo(current);
            if (info.LinkTarget is not null || (info.Exists && info.Attributes.HasFlag(FileAttributes.ReparsePoint)))
                throw new PanelException("UnsafePath", "不支持链接或重解析目录。");
            current = Path.GetDirectoryName(current);
        }
    }

    public static void EnsureIndependent(string candidate, IEnumerable<string> existing)
    {
        var canonical = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar);
        RejectLinks(canonical);
        foreach (var other in existing.Select(p => Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar)))
        {
            if (canonical.Equals(other, Comparison) ||
                canonical.StartsWith(other + Path.DirectorySeparatorChar, Comparison) ||
                other.StartsWith(canonical + Path.DirectorySeparatorChar, Comparison))
                throw new PanelException("SharedInstanceRoot", "实例目录不能相同或相互嵌套。", 409);
        }
    }

    public static void EnsureApproved(string path, IEnumerable<string> roots)
    {
        RejectLinks(path);
        var full = Path.GetFullPath(path);
        if (!roots.Any(root => full.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, Comparison)))
            throw new PanelException("InstancePathUnapproved", "实例目录不在当前批准根目录内，拒绝操作。", 409);
    }
}
