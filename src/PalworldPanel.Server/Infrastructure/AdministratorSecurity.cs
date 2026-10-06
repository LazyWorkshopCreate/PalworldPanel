using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using PalworldPanel.Server.Domain;

namespace PalworldPanel.Server.Infrastructure;

public sealed record AdministratorRecord(string UserName, string PasswordHash, bool SetupPending = false);

public sealed class AdministratorSecurity
{
    private volatile AdministratorRecord administrator;
    private readonly string file;
    private readonly object setupGate = new();
    private readonly string setupToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public bool SetupRequired => administrator.SetupPending;
    public string? SetupToken => SetupRequired ? setupToken : null;
    private readonly PasswordHasher<AdministratorRecord> hasher = CreateHasher();
    private static PasswordHasher<AdministratorRecord> CreateHasher() => new(Options.Create(new PasswordHasherOptions { IterationCount = 600_000 }));
    private readonly ConcurrentDictionary<string, Attempt> attempts = new(StringComparer.Ordinal);
    private sealed record Attempt(int Failures, DateTimeOffset LastUtc, DateTimeOffset? LockedUntil);

    public AdministratorSecurity(string file, bool allowWebSetup = false)
    {
        this.file = file;
        administrator = JsonSerializer.Deserialize<AdministratorRecord>(File.ReadAllText(file), DurableFile.Json)
            ?? throw new PanelException("InvalidAdministrator", "管理员初始化文件无效。", 503);
        if (string.IsNullOrWhiteSpace(administrator.UserName) ||
            (administrator.SetupPending ? !allowWebSetup || administrator.PasswordHash.Length != 0 : string.IsNullOrWhiteSpace(administrator.PasswordHash)))
            throw new PanelException("InvalidAdministrator", "管理员初始化文件无效。", 503);
    }

    public static void PrepareWebSetup(string path)
    {
        using var gate = DiskLock.Acquire(path + ".lock");
        if (File.Exists(path)) throw new PanelException("AdministratorExists", "管理员文件已存在，拒绝覆盖。", 409);
        DurableFile.WriteJson(path, new AdministratorRecord("admin", "", true));
    }

    public void CompleteWebSetup(string token, string password, string confirmation)
    {
        lock (setupGate)
        {
            using var gate = DiskLock.Acquire(file + ".lock", createParent: false);
            var current = JsonSerializer.Deserialize<AdministratorRecord>(File.ReadAllText(file), DurableFile.Json);
            if (current?.SetupPending != true || !SetupRequired)
                throw new PanelException("AdministratorExists", "管理员已设置，请登录。", 409);
            if (string.IsNullOrEmpty(token) || token.Length != setupToken.Length || !CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(token), System.Text.Encoding.UTF8.GetBytes(setupToken)))
                throw new PanelException("InvalidSetupToken", "设置页面已过期，请刷新后重试。", 403);
            if (password is null || password != confirmation || password.Length is < 16 or > 128)
                throw new PanelException("WeakCredential", "密码须为 16–128 字符，且两次输入一致。", 400);
            var record = new AdministratorRecord("admin", "");
            record = record with { PasswordHash = hasher.HashPassword(record, password) };
            DurableFile.WriteJson(file, record);
            administrator = record;
        }
    }

    public static void Initialize(string path, string user, string password)
    {
        if (File.Exists(path)) throw new PanelException("AdministratorExists", "初始化文件已存在，拒绝覆盖。", 409);
        if (string.IsNullOrWhiteSpace(user) || user.Length > 64 || password.Length is < 16 or > 128)
            throw new PanelException("WeakCredential", "管理员初始密码至少 16 字符。", 400);
        var hasher = CreateHasher();
        var admin = new AdministratorRecord(user, "");
        DurableFile.WriteJson(path, admin with { PasswordHash = hasher.HashPassword(admin, password) });
    }

    public ClaimsPrincipal Authenticate(string user, string password, string source)
    {
        if (SetupRequired) throw new PanelException("SetupRequired", "请先设置管理员密码。", 409);
        var now = DateTimeOffset.UtcNow;
        foreach (var key in new[] { "account", "source:" + source })
            if (attempts.TryGetValue(key, out var a) && a.LockedUntil > now)
                throw new PanelException("LoginLimited", "登录尝试过多，请稍后重试。", 429);
        var correctPassword = password.Length <= 128 &&
            hasher.VerifyHashedPassword(administrator, administrator.PasswordHash, password) != PasswordVerificationResult.Failed;
        if (user != administrator.UserName || !correctPassword)
        {
            foreach (var key in new[] { "account", "source:" + source })
                attempts.AddOrUpdate(key, new Attempt(1, now, null), (_, a) =>
                {
                    var failures = now - a.LastUtc > TimeSpan.FromMinutes(15) ? 1 : a.Failures + 1;
                    return new(failures, now, failures >= 5 ? now.AddMinutes(15) : null);
                });
            throw new PanelException("LoginFailed", "账号或密码不正确。", 401);
        }
        attempts.TryRemove("account", out _);
        attempts.TryRemove("source:" + source, out _);
        return new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.Name, administrator.UserName),
            new Claim("authenticatedAt", now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Claim("csrf", Convert.ToHexString(RandomNumberGenerator.GetBytes(32)))
        ], "PanelCookie"));
    }

    public static void RequireRecent(ClaimsPrincipal user)
    {
        if (!long.TryParse(user.FindFirstValue("authenticatedAt"), out var seconds) ||
            DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(seconds) > TimeSpan.FromMinutes(5))
            throw new PanelException("ReauthenticationRequired", "请重新登录后执行危险操作。", 403);
    }
}
