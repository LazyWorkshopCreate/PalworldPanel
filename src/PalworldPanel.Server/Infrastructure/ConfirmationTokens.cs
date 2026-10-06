using System.Collections.Concurrent;
using System.Security.Cryptography;
using PalworldPanel.Server.Domain;

namespace PalworldPanel.Server.Infrastructure;

public sealed record Confirmation(string Token, string User, string InstanceId, string Action, int Revision,
    string PreviewHash, DateTimeOffset ExpiresUtc);

public sealed class ConfirmationTokens
{
    private readonly ConcurrentDictionary<string, Confirmation> tokens = new(StringComparer.Ordinal);
    public Confirmation Issue(string user, string instance, string action, int revision, string hash)
    {
        foreach (var item in tokens.Where(t => t.Value.ExpiresUtc < DateTimeOffset.UtcNow)) tokens.TryRemove(item.Key, out _);
        if (tokens.Count >= 1024) throw new PanelException("PreviewLimit", "预览请求过多，请稍后重试。", 429);
        var result = new Confirmation(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), user, instance,
            action, revision, hash, DateTimeOffset.UtcNow.AddMinutes(10));
        tokens[result.Token] = result;
        return result;
    }
    public void Consume(string token, string user, string instance, string action, int revision, string hash)
    {
        if (!tokens.TryRemove(token, out var confirmation) || confirmation.User != user ||
            confirmation.InstanceId != instance || confirmation.Action != action || confirmation.Revision != revision ||
            confirmation.PreviewHash != hash || confirmation.ExpiresUtc < DateTimeOffset.UtcNow)
            throw new PanelException("PreviewExpired", "确认预览无效、已使用或已改变，请重新预检。", 409);
    }
}
