using System.Runtime.InteropServices;
using PalworldPanel.Server.Domain;

namespace PalworldPanel.Server.Infrastructure;

public static class TreeFiles
{
    public static IEnumerable<string> Files(string root)
    {
        SafePaths.RejectLinks(root);
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(pending.Pop()))
            {
                SafePaths.RejectLinks(entry);
                if (Directory.Exists(entry)) pending.Push(entry);
                else yield return entry;
            }
        }
    }
    public static long Bytes(string root) => Directory.Exists(root) ? Files(root).Sum(file => new FileInfo(file).Length) : 0;
    public static void Copy(string source, string destination)
    {
        SafePaths.RejectLinks(source);
        SafePaths.RejectLinks(destination);
        Directory.CreateDirectory(destination);
        foreach (var file in Files(source))
        {
            var target = SafePaths.Within(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = File.OpenRead(file);
            using var output = DurableFile.CreatePrivateFile(target);
            input.CopyTo(output);
            output.Flush(true);
            if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(file) &
                (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0)
                File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            DurableFile.FlushDirectory(Path.GetDirectoryName(target)!);
        }
        DurableFile.FlushDirectory(destination);
    }
    public static void PrepareGameOwnership(string root)
    {
        if (!OperatingSystem.IsLinux()) return;
        SafePaths.RejectLinks(root);
        foreach (var file in Files(root))
        {
            if (chown(file, 1000, 1000) != 0) throw new PanelException("OwnershipFailed", "无法设置游戏文件归属。", 409);
            var executable = (File.GetUnixFileMode(file) & UnixFileMode.UserExecute) != 0;
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | (executable ? UnixFileMode.UserExecute : 0));
        }
        var directories = new HashSet<string>(StringComparer.Ordinal) { root };
        foreach (var file in Files(root))
        {
            var parent = Path.GetDirectoryName(file);
            while (parent is not null && (parent == root || parent.StartsWith(root + "/", StringComparison.Ordinal)))
            { directories.Add(parent); parent = Path.GetDirectoryName(parent); }
        }
        foreach (var directory in directories)
        {
            if (chown(directory, 1000, 1000) != 0) throw new PanelException("OwnershipFailed", "无法设置游戏目录归属。", 409);
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
    [DllImport("libc", SetLastError = true)] private static extern int chown(string path, uint owner, uint group);
}
