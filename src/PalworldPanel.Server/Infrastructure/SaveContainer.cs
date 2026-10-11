using System.Buffers.Binary;
using System.IO.Compression;
using PalworldPanel.Server.Domain;

namespace PalworldPanel.Server.Infrastructure;

public sealed record SaveContainerInfo(string Kind, int? SaveType, bool Gvas, string? Reason)
{
    public bool ContentWritable => false;
}

public static class SaveContainer
{
    public const long MaxBodyBytes = 32L * 1024 * 1024;

    public static SaveContainerInfo Describe(ReadOnlySpan<byte> file, long fileLength)
    {
        if (!TryHeader(file, out var header, out _, out var kind))
            return new(kind, null, false, "存档容器无法识别，拒绝改写。");
        if (header.Magic == "PlM") return new("PlM", header.SaveType, false, "当前压缩不能由面板解码，拒绝改写内容。");
        if (header.Magic != "PlZ" || header.SaveType is not (0x31 or 0x32))
            return new(kind, header.SaveType, false, "存档容器不受支持，拒绝改写。");
        if (header.UncompressedLength > MaxBodyBytes || fileLength > MaxBodyBytes)
            return new("PlZ", header.SaveType, false, "存档超过 32 MiB，本版只识别文件头。");
        if (file.Length != fileLength) return new("PlZ", header.SaveType, false, "存档正文尚未读取。");
        try
        {
            var body = DecodePlZ(file);
            var gvas = body.Length >= 4 && body.AsSpan(0, 4).SequenceEqual("GVAS"u8);
            return new("PlZ", header.SaveType, gvas, gvas ? null : "解压结果不是存档正文，拒绝改写。");
        }
        catch (PanelException error)
        {
            return new("PlZ", header.SaveType, false, error.Message);
        }
    }

    public static byte[] DecodePlZ(ReadOnlySpan<byte> file)
    {
        if (!TryHeader(file, out var header, out var offset, out _) || header.Magic != "PlZ")
            throw new PanelException("UnsupportedSave", "只有 zlib 存档容器可以解码。", 409);
        return Decode(file, offset, header);
    }

    public static byte[] EncodePlZ(ReadOnlySpan<byte> body, byte saveType)
    {
        if (saveType is not (0x31 or 0x32) || body.Length == 0 || body.Length > MaxBodyBytes)
            throw new PanelException("UnsupportedSave", "不能封装该存档正文。", 400);
        var payload = Deflate(body);
        if (saveType == 0x32) payload = Deflate(payload);
        var file = new byte[12 + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(file, (uint)body.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4), (uint)payload.Length);
        "PlZ"u8.CopyTo(file.AsSpan(8));
        file[11] = saveType;
        payload.CopyTo(file.AsSpan(12));
        return file;
    }

    private static byte[] Decode(ReadOnlySpan<byte> file, int offset, SaveHeader header)
    {
        if (header.SaveType is not (0x31 or 0x32)) throw new PanelException("UnsupportedSave", "存档压缩类型不受支持。", 409);
        if (header.UncompressedLength == 0 || header.UncompressedLength > MaxBodyBytes)
            throw new PanelException("SaveTooLarge", "存档声明的大小不能读取。", 409);
        var payload = file[(offset + 12)..];
        if (payload.Length != header.CompressedLength) throw new PanelException("SaveLengthMismatch", "存档压缩长度与文件不符。", 409);
        var body = Inflate(payload, (int)MaxBodyBytes);
        if (header.SaveType == 0x32) body = Inflate(body, (int)header.UncompressedLength);
        if (body.Length != header.UncompressedLength) throw new PanelException("SaveLengthMismatch", "存档解压长度与声明不符。", 409);
        return body;
    }

    private static bool TryHeader(ReadOnlySpan<byte> file, out SaveHeader header, out int offset, out string kind)
    {
        header = default;
        offset = 0;
        kind = "Unknown";
        if (file.Length < 12) return false;
        if (!TryRead(file, 0, out header)) return false;
        if (header.Magic == "CNK")
        {
            kind = "CNK";
            if (file.Length < 24 || !TryRead(file, 12, out header)) return false;
            offset = 12;
        }
        kind = header.Magic is "PlZ" or "PlM" ? header.Magic : kind;
        return header.Magic is "PlZ" or "PlM";
    }

    private static bool TryRead(ReadOnlySpan<byte> file, int offset, out SaveHeader header)
    {
        header = default;
        if (file.Length < offset + 12) return false;
        var magic = file.Slice(offset + 8, 3);
        header = new(BinaryPrimitives.ReadUInt32LittleEndian(file[offset..]), BinaryPrimitives.ReadUInt32LittleEndian(file[(offset + 4)..]),
            System.Text.Encoding.ASCII.GetString(magic), file[offset + 11]);
        return true;
    }

    private static byte[] Inflate(ReadOnlySpan<byte> source, int limit)
    {
        using var input = new MemoryStream(source.ToArray());
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var count = zlib.Read(buffer);
            if (count == 0) break;
            if (output.Length + count > limit) throw new PanelException("SaveTooLarge", "存档解压超过读取上限。", 409);
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    private static byte[] Deflate(ReadOnlySpan<byte> source)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            zlib.Write(source);
        return output.ToArray();
    }

    private readonly record struct SaveHeader(uint UncompressedLength, uint CompressedLength, string Magic, byte SaveType);
}
