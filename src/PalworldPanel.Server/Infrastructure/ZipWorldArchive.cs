using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using PalworldPanel.Server.Domain;

namespace PalworldPanel.Server.Infrastructure;

public sealed record ZipLimits(long UploadBytes = 4L << 30, long ExpandedBytes = 20L << 30,
    int Files = 100_000, long FileBytes = 8L << 30, double Ratio = 200);
public sealed record ZipWorldPlan(string Sha256, string[] Worlds, long ExpandedBytes, int Files, string[] Ignored);

public static partial class ZipWorldArchive
{
    public static async Task<ZipWorldPlan> InspectAsync(string path, ZipLimits limits, CancellationToken cancellation = default)
    {
        try { return await InspectCoreAsync(path, limits, cancellation); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException)
        { throw Error("ZIP 损坏、加密或压缩格式不受支持。"); }
    }

    private static async Task<ZipWorldPlan> InspectCoreAsync(string path, ZipLimits limits, CancellationToken cancellation)
    {
        SafePaths.RejectLinks(path);
        if (new FileInfo(path).Length > limits.UploadBytes) throw Error("ZIP 超过上传限制。");
        var digest = await HashAsync(path, cancellation);
        using var archive = ZipFile.OpenRead(path);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var worlds = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var ignored = new HashSet<string>(StringComparer.Ordinal);
        bool? prefixedRoot = null;
        long total = 0;
        if (archive.Entries.Count > limits.Files) throw Error("ZIP 条目数超过限制。");
        foreach (var entry in archive.Entries)
        {
            var name = Normalize(entry.FullName);
            var usesSavedRoot = name.StartsWith("Saved/", StringComparison.Ordinal);
            if (prefixedRoot is not null && prefixedRoot != usesSavedRoot) throw Error("ZIP 不能混用 Saved 与直接 SaveGames 根目录。");
            prefixedRoot = usesSavedRoot;
            if (!seen.Add(name.TrimEnd('/'))) throw Error("ZIP 存在重复或大小写冲突条目。");
            var type = (entry.ExternalAttributes >> 16) & 0xf000;
            if (type != 0 && type != 0x8000 && type != 0x4000) throw Error("ZIP 包含链接或特殊文件。");
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) continue;
            if (entry.Length > limits.FileBytes || entry.Length < 0 ||
                entry.Length > Math.Max(1, entry.CompressedLength) * limits.Ratio) throw Error("ZIP 文件或压缩比超过限制。");
            if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
                throw Error("不允许嵌套归档。");
            files.Add(name);
            var relative = name.StartsWith("Saved/", StringComparison.Ordinal) ? name[6..] : name;
            if (relative.StartsWith("SaveGames/0/", StringComparison.Ordinal))
            {
                var parts = relative.Split('/');
                if (parts.Length < 4 || !WorldId().IsMatch(parts[2])) throw Error("世界目录结构无效。");
                var member = string.Join('/', parts.Skip(3));
                if (!AllowedMember(member) && !member.StartsWith("backup/", StringComparison.Ordinal)) throw Error("世界包含未经支持的附件。");
                if (member.StartsWith("backup/", StringComparison.Ordinal)) ignored.Add("SaveGames backup");
                if (!worlds.TryGetValue(parts[2], out var members)) worlds[parts[2]] = members = new(StringComparer.OrdinalIgnoreCase);
                if (worlds.Keys.Any(world => string.Equals(world, parts[2], StringComparison.OrdinalIgnoreCase) && world != parts[2]))
                    throw Error("世界目录存在大小写冲突。");
                if (!member.StartsWith("backup/", StringComparison.Ordinal)) members.Add(member);
            }
            else if (relative.StartsWith("Config/", StringComparison.Ordinal) || relative.StartsWith("Logs/", StringComparison.Ordinal) ||
                     relative.StartsWith("Crashes/", StringComparison.Ordinal)) ignored.Add(relative.Split('/')[0]);
            else throw Error("ZIP 顶层结构不受支持。");
            await using var stream = entry.Open();
            var buffer = new byte[81920];
            long written = 0;
            uint crc = 0xffffffff;
            int count;
            while ((count = await stream.ReadAsync(buffer, cancellation)) != 0)
            {
                written += count;
                total += count;
                if (written > limits.FileBytes || total > limits.ExpandedBytes || written > entry.Length) throw Error("ZIP 实际展开量超过限制。");
                crc = UpdateCrc(crc, buffer.AsSpan(0, count));
            }
            if (written != entry.Length || ~crc != entry.Crc32) throw Error("ZIP 内容长度或 CRC 校验失败。");
        }
        foreach (var name in files)
        {
            var parts = name.Split('/');
            for (var i = 1; i < parts.Length; i++)
                if (files.Contains(string.Join('/', parts.Take(i)))) throw Error("ZIP 文件与目录冲突。");
        }
        if (worlds.Count == 0 || worlds.Values.Any(m => !m.Contains("Level.sav") || !m.Contains("LevelMeta.sav")))
            throw Error("ZIP 缺少完整世界文件。");
        return new ZipWorldPlan(digest, worlds.Keys.ToArray(), total, files.Count, ignored.ToArray());
    }

    public static async Task ExtractWorldAsync(string path, ZipWorldPlan plan, string world, string destination,
        ZipLimits limits, CancellationToken cancellation = default)
    {
        if (!plan.Worlds.Contains(world, StringComparer.Ordinal) || await HashAsync(path, cancellation) != plan.Sha256)
            throw new PanelException("UploadChanged", "上传包或世界选择已改变。", 409);
        var validated = await InspectAsync(path, limits, cancellation);
        if (validated.Sha256 != plan.Sha256) throw new PanelException("UploadChanged", "上传包已改变。", 409);
        SafePaths.RejectLinks(destination);
        if (Directory.Exists(destination)) throw new PanelException("StagingExists", "暂存目录已经存在。", 409);
        Directory.CreateDirectory(destination);
        using var archive = ZipFile.OpenRead(path);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            var name = Normalize(entry.FullName);
            if (name.StartsWith("Saved/", StringComparison.Ordinal)) name = name[6..];
            var prefix = $"SaveGames/0/{world}/";
            if (!name.StartsWith(prefix, StringComparison.Ordinal) || name.EndsWith('/') || name[prefix.Length..].StartsWith("backup/", StringComparison.Ordinal)) continue;
            var target = SafePaths.Within(destination, name[prefix.Length..]);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = entry.Open();
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var buffer = new byte[81920];
            long length = 0;
            int count;
            while ((count = await input.ReadAsync(buffer, cancellation)) != 0)
            {
                total += count;
                length += count;
                if (length > limits.FileBytes || total > limits.ExpandedBytes) throw Error("实际展开量超限。");
                await output.WriteAsync(buffer.AsMemory(0, count), cancellation);
            }
            output.Flush(true);
        }
        DurableFile.FlushDirectory(destination);
    }

    public static async Task<string> HashAsync(string path, CancellationToken cancellation = default)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellation));
    }

    private static string Normalize(string value)
    {
        var normalized = value.Replace('\\', '/');
        if (normalized.StartsWith('/') || normalized.Contains(':') || normalized.Any(char.IsControl) ||
            normalized.Split('/').Any(p => p is ".." or ".") || normalized.Contains("//")) throw Error("ZIP 路径不安全。");
        if (string.IsNullOrEmpty(normalized.TrimEnd('/'))) throw Error("ZIP 路径为空。");
        return normalized;
    }

    private static bool AllowedMember(string member) => member is "Level.sav" or "LevelMeta.sav" or "_dps.sav" ||
        PlayerFile().IsMatch(member) || WorldAttachment().IsMatch(member);
    private static PanelException Error(string message) => new("UnsafeArchive", message);
    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320 : 0);
        }
        return crc;
    }

    [GeneratedRegex("^[a-fA-F0-9]{32}$")] private static partial Regex WorldId();
    [GeneratedRegex("^Players/[a-fA-F0-9]{32}\\.sav$")] private static partial Regex PlayerFile();
    [GeneratedRegex("^[a-fA-F0-9]{32}_dps\\.sav$")] private static partial Regex WorldAttachment();
}
