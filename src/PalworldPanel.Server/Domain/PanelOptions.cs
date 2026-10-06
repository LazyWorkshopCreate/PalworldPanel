using System.Net;
using System.Text.Json;
using PalworldPanel.Server.Infrastructure;

namespace PalworldPanel.Server.Domain;

public sealed record PanelOptions(string BindIp, int Port, string[] AllowedIps, string StateRoot,
    string[] InstanceRoots, string BackupRoot, string KeyFile, string AdministratorFile,
    string CertificateFile, string CertificatePasswordFile, string DefaultImage,
    string[] AllowedImages, long MinimumMemoryMiB = 16384, long ReservedMemoryMiB = 4096,
    int GamePortStart = 18211, int RestPortStart = 18312, int QueryPortStart = 18415,
    string? DockerHostRoot = null, string? ContainerMountRoot = null, bool DesktopValidation = false,
    bool DesktopAllowHttp = false, string? DockerExecutable = null, string? DockerEndpoint = null,
    string? DockerConfigDirectory = null, bool AllowHttp = false, bool AllowWebSetup = false)
{
    public bool UsesHttp => AllowHttp || DesktopAllowHttp;

    public static PanelOptions Load(string path)
    {
        SafePaths.RejectLinks(path);
        var options = JsonSerializer.Deserialize<PanelOptions>(File.ReadAllText(path), DurableFile.Json)
            ?? throw new PanelException("InvalidConfiguration", "配置格式无效。", 503);
        options.Validate();
        return options;
    }

    public void Validate()
    {
        if (DockerExecutable is not null && (!Path.IsPathFullyQualified(DockerExecutable) || !File.Exists(DockerExecutable)))
            throw new PanelException("InvalidConfiguration", "Docker CLI 必须是存在的绝对路径。", 503);
        if (DockerEndpoint is not null && DockerEndpoint is not ("npipe:////./pipe/dockerDesktopLinuxEngine" or "unix:///var/run/docker.sock"))
            throw new PanelException("InvalidConfiguration", "Docker 只允许本机 Linux 引擎，不开放 TCP 管理。", 503);
        if (DockerConfigDirectory is not null)
        {
            if (!Path.IsPathFullyQualified(DockerConfigDirectory)) throw new PanelException("InvalidConfiguration", "Docker 配置目录必须是绝对路径。", 503);
            SafePaths.RejectLinks(DockerConfigDirectory);
        }
        if (DesktopAllowHttp && (!DesktopValidation || BindIp != "172.30.88.1" ||
            AllowedIps.Any(ip => ip is not ("172.30.88.10" or "172.30.88.5"))))
            throw new PanelException("InvalidConfiguration", "HTTP 仅限专用 Docker Desktop 隔离测试网络。", 503);
        if (!IPAddress.TryParse(BindIp, out var bind) || !SourceAccessPolicy.IsPrivate(bind) || Port is < 1024 or > 65535)
            throw new PanelException("InvalidConfiguration", "必须明确绑定内网 IPv4 地址。", 503);
        _ = new SourceAccessPolicy(AllowedIps);
        if (InstanceRoots.Length == 0 || MinimumMemoryMiB < 512 || ReservedMemoryMiB < 512)
            throw new PanelException("InvalidConfiguration", "实例目录或容量预算配置无效。", 503);
        if (!DesktopValidation && (MinimumMemoryMiB < 16384 || ReservedMemoryMiB < 4096))
            throw new PanelException("InvalidConfiguration", "生产预算必须保留 4 GiB，实例至少 16 GiB。", 503);
        foreach (var path in InstanceRoots.Append(StateRoot).Append(BackupRoot))
        {
            if (!Path.IsPathRooted(path)) throw new PanelException("InvalidConfiguration", "配置根目录必须是绝对路径。", 503);
            SafePaths.RejectLinks(path);
            if (!Directory.Exists(path))
            {
                if (OperatingSystem.IsWindows()) Directory.CreateDirectory(path);
                else Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }
        var allRoots = InstanceRoots.Append(StateRoot).Append(BackupRoot).ToArray();
        for (var i = 0; i < allRoots.Length; i++) SafePaths.EnsureIndependent(allRoots[i], allRoots.Take(i));
        foreach (var secret in (UsesHttp ? new[] { KeyFile, AdministratorFile } : new[] { KeyFile, AdministratorFile, CertificateFile, CertificatePasswordFile }))
        {
            if (!Path.IsPathRooted(secret)) throw new PanelException("InvalidConfiguration", "安全文件必须采用绝对路径。", 503);
            SafePaths.RejectLinks(secret);
            if (!File.Exists(secret)) throw new PanelException("MissingSecret", "必要安全文件不存在，拒绝启动。", 503);
            if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(secret) &
                (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
                throw new PanelException("SecretPermissions", "安全文件只能由服务所有者读取，请修正权限后重试。", 503);
        }
        if (!AllowedImages.Contains(DefaultImage, StringComparer.Ordinal) || AllowedImages.Any(image => !System.Text.RegularExpressions.Regex.IsMatch(image,
            "^[a-z0-9][a-z0-9./:_-]*@sha256:[a-f0-9]{64}$")))
            throw new PanelException("InvalidConfiguration", "默认镜像必须在批准清单中。", 503);
    }
}
