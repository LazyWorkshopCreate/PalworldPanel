using System.Runtime.InteropServices;
using PalworldPanel.Server.Domain;

namespace PalworldPanel.Server.Infrastructure;

public sealed class DiskLock : IDisposable
{
    private readonly FileStream stream;
    private DiskLock(FileStream stream) => this.stream = stream;

    public static DiskLock AcquireInstance(string stateRoot, string instanceRoot, string id, bool requireRoot = true)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new PanelException("InvalidInstance", "实例标识无效。", 400);
        SafePaths.RejectLinks(instanceRoot);
        if (requireRoot && !Directory.Exists(instanceRoot))
            throw new PanelException("InstanceDirectoryMissing", "实例目录不存在，禁止隐式创建空目录。", 409);
        // Stable identity survives quarantine moves on Windows and never locks a file
        // inside the tree that the owning task must rename or delete.
        return Acquire(SafePaths.Within(stateRoot, "instance-locks/" + id + ".lock"));
    }

    public static DiskLock Acquire(string path, bool createParent = true)
    {
        SafePaths.RejectLinks(path);
        var parent = Path.GetDirectoryName(path)!;
        if (createParent) Directory.CreateDirectory(parent);
        else if (!Directory.Exists(parent)) throw new PanelException("InstanceDirectoryMissing", "实例目录不存在，禁止隐式创建空目录。", 409);
        FileStream stream;
        try
        {
            stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                OperatingSystem.IsLinux() ? FileShare.ReadWrite : FileShare.None);
        }
        catch (IOException) { throw new PanelException("TaskConflict", "实例正在由其他执行器操作。", 409); }
        if (OperatingSystem.IsLinux() && flock(stream.SafeFileHandle.DangerousGetHandle().ToInt32(), 6) != 0)
        {
            stream.Dispose();
            throw new PanelException("TaskConflict", "实例正在由其他执行器操作。", 409);
        }
        return new DiskLock(stream);
    }

    public void Dispose() => stream.Dispose();
    [DllImport("libc", SetLastError = true)] private static extern int flock(int descriptor, int operation);
}
