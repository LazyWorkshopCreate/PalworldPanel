using System.Security.Cryptography;
using PalworldPanel.Server.Infrastructure;
using Xunit;

namespace PalworldPanel.UnitTests;

public sealed class EncryptedArchiveTests
{
    [Fact]
    public async Task MultiChunkExportAuthenticatesContentPasswordAndTermination()
    {
        var original = RandomNumberGenerator.GetBytes((1 << 20) + 23);
        await using var encrypted = new MemoryStream();
        await EncryptedArchive.EncryptAsync(new MemoryStream(original), encrypted, "synthetic-export-password");
        encrypted.Position = 0;
        await using var result = new MemoryStream();
        await EncryptedArchive.DecryptAsync(encrypted, result, "synthetic-export-password");
        Assert.Equal(original, result.ToArray());
        encrypted.Position = 0;
        await Assert.ThrowsAnyAsync<CryptographicException>(() => EncryptedArchive.DecryptAsync(encrypted, new MemoryStream(), "incorrect-test-password"));
        var bytes = encrypted.ToArray();
        await Assert.ThrowsAsync<EndOfStreamException>(() => EncryptedArchive.DecryptAsync(new MemoryStream(bytes[..^24]), new MemoryStream(), "synthetic-export-password"));
        bytes[40] ^= 1;
        await Assert.ThrowsAnyAsync<CryptographicException>(() => EncryptedArchive.DecryptAsync(new MemoryStream(bytes), new MemoryStream(), "synthetic-export-password"));
    }
}
