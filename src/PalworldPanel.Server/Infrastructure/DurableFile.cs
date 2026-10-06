using System.Runtime.InteropServices;
using System.Text.Json;

namespace PalworldPanel.Server.Infrastructure;

public static class DurableFile
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public static readonly JsonSerializerOptions JsonCompact = new(JsonSerializerDefaults.Web);

    public static void WriteJson<T>(string path, T value) => Write(path, JsonSerializer.SerializeToUtf8Bytes(value, Json));
    public static FileStream CreatePrivateFile(string path, FileAccess access = FileAccess.Write)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = access, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return new FileStream(path, options);
    }

    public static void Write(string path, ReadOnlySpan<byte> content)
    {
        SafePaths.RejectLinks(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".partial." + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = CreatePrivateFile(temporary))
            {
                stream.Write(content);
                stream.Flush(true);
            }
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temporary, path, true);
            FlushDirectory(Path.GetDirectoryName(path)!);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static void FlushDirectory(string directory)
    {
        if (!OperatingSystem.IsLinux()) return;
        var descriptor = open(directory, 65536);
        if (descriptor < 0) throw new IOException("不能打开目录进行持久化。");
        try { if (fsync(descriptor) != 0) throw new IOException("不能持久化目录更新。"); }
        finally { close(descriptor); }
    }

    [DllImport("libc", SetLastError = true)] private static extern int open(string path, int flags);
    [DllImport("libc", SetLastError = true)] private static extern int fsync(int descriptor);
    [DllImport("libc", SetLastError = true)] private static extern int close(int descriptor);
}
