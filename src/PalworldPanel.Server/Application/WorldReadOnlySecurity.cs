using System.Security.Cryptography;
using System.Text;
using PalworldPanel.Server.Domain;
using PalworldPanel.Server.Infrastructure;

namespace PalworldPanel.Server.Application;

public sealed class WorldReadOnlySecurity(SqliteStore store, SecretVault vault, TimeProvider? clock = null)
{
    public const string Cookie = "PalworldPanel.WorldView";
    public const string Path = "/api/v1/world-view";
    private sealed record Session(string InstanceId, string World, string Cipher, DateTimeOffset Expires);
    private sealed record Attempt(int Failures, DateTimeOffset Expires);
    private readonly Dictionary<string, Session> sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Attempt> attempts = new(StringComparer.Ordinal);
    private readonly object gate = new();
    public static string? Normalize(string? world) => world is { Length: <= 36 } && Guid.TryParse(world, out var id) ? id.ToString("N") : null;
    private static PanelException Denied() => new("WorldAccessDenied", "世界 Id 或游戏连接密码不可用。", 401);
    public static bool Eligible(InstanceRecord i) => i.Applied is not null && i.WorldGuid is not null && i.QuarantinedUtc is null && i.PurgedUtc is null;
    public static string AppliedCipher(InstanceRecord i) => i.AppliedGameCipher ?? i.GameCipher;
    public static bool PasswordMatches(InstanceRecord instance, string? password, SecretVault vault)
    {
        if (!Eligible(instance) || password is not { Length: > 0 and <= 128 }) return false;
        var expected = vault.Open(AppliedCipher(instance), instance.Id + ":game");
        return expected.Length > 0 && CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(expected)), SHA256.HashData(Encoding.UTF8.GetBytes(password)));
    }
    private InstanceRecord? Find(string? world)
    {
        if (world is null) return null;
        var matches = store.Instances().Where(i => Eligible(i) && Normalize(i.WorldGuid) == world).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
    public string Login(string? worldId, string? password, string source)
    {
        lock (gate)
        {
            var now = (clock ?? TimeProvider.System).GetUtcNow();
            foreach (var key in attempts.Where(p => p.Value.Expires <= now).Select(p => p.Key).ToArray()) attempts.Remove(key);
            foreach (var key in sessions.Where(p => p.Value.Expires <= now).Select(p => p.Key).ToArray()) sessions.Remove(key);
            if (attempts.TryGetValue(source, out var attempt) && attempt.Failures >= 5 || !attempts.ContainsKey(source) && attempts.Count >= 4096)
                throw new PanelException("WorldLoginLimited", "访问尝试过多，请稍后重试。", 429);
            var world = Normalize(worldId);
            var instance = Find(world);
            if (instance is null || !PasswordMatches(instance, password, vault))
            {
                attempts[source] = new((attempt?.Failures ?? 0) + 1, attempt?.Expires ?? now.AddMinutes(15));
                throw Denied();
            }
            if (sessions.Count >= 1024) throw new PanelException("WorldSessionsLimited", "只读会话已满，请稍后重试。", 429);
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            sessions.Add(token, new(instance.Id, world!, AppliedCipher(instance), now.AddMinutes(30)));
            return token;
        }
    }
    public InstanceRecord Require(string? token, string worldId)
    {
        lock (gate)
        {
            if (token is null || !sessions.TryGetValue(token, out var session)) throw Denied();
            var instance = Find(session.World);
            if (session.Expires <= (clock ?? TimeProvider.System).GetUtcNow() || instance is null || instance.Id != session.InstanceId || AppliedCipher(instance) != session.Cipher)
            { sessions.Remove(token); throw Denied(); }
            if (Normalize(worldId) != session.World) throw Denied();
            return instance;
        }
    }
    public void Logout(string? token) { if (token is not null) lock (gate) sessions.Remove(token); }
}
