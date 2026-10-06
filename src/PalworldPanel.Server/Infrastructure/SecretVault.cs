using System.Security.Cryptography;
using System.Text;
using PalworldPanel.Server.Domain;

namespace PalworldPanel.Server.Infrastructure;

public sealed class SecretVault
{
    public static bool IsConfigured(string cipher) => !string.IsNullOrEmpty(cipher) && Convert.FromBase64String(cipher).Length > 28;
    private readonly byte[] key;
    public SecretVault(string keyFile)
    {
        SafePaths.RejectLinks(keyFile);
        if (!File.Exists(keyFile)) throw new PanelException("MissingKey", "主密钥不存在，禁止初始化覆盖旧数据。", 503);
        key = File.ReadAllBytes(keyFile);
        if (key.Length != 32) throw new PanelException("InvalidKey", "主密钥格式无效。", 503);
    }

    public string Seal(string value, string purpose)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plain = Encoding.UTF8.GetBytes(value);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plain, cipher, tag, Encoding.UTF8.GetBytes(purpose));
        CryptographicOperations.ZeroMemory(plain);
        return Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray());
    }

    public string Open(string value, string purpose)
    {
        var bytes = Convert.FromBase64String(value);
        if (bytes.Length < 28) throw new CryptographicException("密文格式无效。");
        var plain = new byte[bytes.Length - 28];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(bytes.AsSpan(0, 12), bytes.AsSpan(28), bytes.AsSpan(12, 16), plain, Encoding.UTF8.GetBytes(purpose));
        try { return Encoding.UTF8.GetString(plain); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
}
