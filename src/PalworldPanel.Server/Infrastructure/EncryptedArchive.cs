using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using PalworldPanel.Server.Domain;

namespace PalworldPanel.Server.Infrastructure;

public static class EncryptedArchive
{
    private const int ChunkBytes = 1 << 20;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("PPBAK001");

    public static async Task EncryptAsync(Stream input, Stream output, string password, CancellationToken ct = default)
    {
        ValidatePassphrase(password);
        var header = new byte[32];
        Magic.CopyTo(header, 0);
        RandomNumberGenerator.Fill(header.AsSpan(8));
        var key = Rfc2898DeriveBytes.Pbkdf2(password, header.AsSpan(8, 16), 600_000, HashAlgorithmName.SHA256, 32);
        var buffer = new byte[ChunkBytes];
        try
        {
            using var aes = new AesGcm(key, 16);
            await output.WriteAsync(header, ct);
            uint counter = 0;
            while (true)
            {
                var count = await input.ReadAsync(buffer, ct);
                var frame = new byte[8];
                BinaryPrimitives.WriteUInt32BigEndian(frame, counter);
                BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(4), count);
                var nonce = Nonce(header, counter);
                var cipher = new byte[count];
                var tag = new byte[16];
                aes.Encrypt(nonce, buffer.AsSpan(0, count), cipher, tag, Associated(header, frame));
                await output.WriteAsync(frame, ct);
                await output.WriteAsync(cipher, ct);
                await output.WriteAsync(tag, ct);
                if (count == 0) break;
                counter = checked(counter + 1);
            }
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(buffer); }
    }

    public static async Task DecryptAsync(Stream input, Stream output, string password, CancellationToken ct = default)
    {
        ValidatePassphrase(password);
        var header = new byte[32];
        await input.ReadExactlyAsync(header, ct);
        if (!header.AsSpan(0, 8).SequenceEqual(Magic)) throw new PanelException("InvalidExport", "导出格式不受支持。");
        var key = Rfc2898DeriveBytes.Pbkdf2(password, header.AsSpan(8, 16), 600_000, HashAlgorithmName.SHA256, 32);
        try
        {
            using var aes = new AesGcm(key, 16);
            uint counter = 0;
            while (true)
            {
                var frame = new byte[8];
                await input.ReadExactlyAsync(frame, ct);
                var count = BinaryPrimitives.ReadInt32BigEndian(frame.AsSpan(4));
                if (BinaryPrimitives.ReadUInt32BigEndian(frame) != counter || count is < 0 or > ChunkBytes)
                    throw new PanelException("InvalidExport", "导出帧序号或长度异常。");
                var cipher = new byte[count];
                var tag = new byte[16];
                await input.ReadExactlyAsync(cipher, ct);
                await input.ReadExactlyAsync(tag, ct);
                var plain = new byte[count];
                try
                {
                    aes.Decrypt(Nonce(header, counter), cipher, tag, plain, Associated(header, frame));
                    if (count == 0)
                    {
                        if (input.ReadByte() != -1) throw new PanelException("InvalidExport", "终止帧后存在未知内容。");
                        break;
                    }
                    await output.WriteAsync(plain, ct);
                }
                finally { CryptographicOperations.ZeroMemory(plain); }
                counter = checked(counter + 1);
            }
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private static byte[] Nonce(byte[] header, uint counter)
    {
        var nonce = new byte[12];
        header.AsSpan(24, 8).CopyTo(nonce);
        BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(8), counter);
        return nonce;
    }
    private static byte[] Associated(byte[] header, byte[] frame) => header.Concat(frame).ToArray();
    public static void ValidatePassphrase(string? password)
    { if (password is null || password.Length is < 16 or > 128) throw new PanelException("WeakExportPassword", "导出口令须为 16 至 128 字符。", 400); }
}
