using System.Security.Cryptography;
using System.Text.Json;
using PalworldPanel.Server.Domain;
using PalworldPanel.Server.Infrastructure;

namespace PalworldPanel.Server.Application;

public sealed record GameUpdatePlan(string InstanceId, int Revision, string? SourceHash, string Image,
    string CurrentManifest, string TargetManifest, string TargetBuild, string? TargetGameVersion, DateTimeOffset ExpiresUtc);
public sealed record GameUpdateCheck(string Status, string CurrentVersion, string TargetVersion, DateTimeOffset CheckedUtc, string? UpdateToken);

public sealed class GameUpdateService(SteamReleaseCatalog catalog, SecretVault vault)
{
    public async Task<GameUpdateCheck> CheckAsync(InstanceRecord instance, CancellationToken cancellation)
    {
        var current = Read(instance);
        var latest = await catalog.LatestAsync(cancellation);
        var now = DateTimeOffset.UtcNow;
        var target = latest.GameVersion ?? $"正式版 {latest.BuildId}";
        if (current.DepotManifest == latest.DepotManifest) return new("up-to-date", instance.GameBuild ?? target, target, now, null);
        if (latest.GameVersion is null)
            throw new PanelException("UpdateVersionUnavailable", "发现安装更新，但暂时无法确认目标版本，请稍后重新检查。", 503);
        if (ulong.Parse(current.BuildId) > ulong.Parse(latest.BuildId))
            throw new PanelException("NewerInstallation", "此实例安装版本高于当前正式版本，不能自动降级。", 409);
        var plan = new GameUpdatePlan(instance.Id, instance.Revision, instance.SourceHash, instance.Image,
            current.DepotManifest, latest.DepotManifest, latest.BuildId, latest.GameVersion, now.AddMinutes(10));
        return new("available", instance.GameBuild ?? $"正式版 {current.BuildId}", target, now,
            vault.Seal(JsonSerializer.Serialize(plan, DurableFile.JsonCompact), instance.Id + ":game-update"));
    }

    public async Task<GameUpdatePlan> ValidateAsync(InstanceRecord instance, JsonElement? arguments, CancellationToken cancellation)
    {
        GameUpdatePlan plan;
        try
        {
            if (arguments is not { ValueKind: JsonValueKind.Object } payload || !payload.TryGetProperty("updateToken", out var value) ||
                value.ValueKind != JsonValueKind.String || value.GetString() is not { Length: > 0 and < 8192 } token)
                throw new FormatException();
            plan = JsonSerializer.Deserialize<GameUpdatePlan>(vault.Open(token, instance.Id + ":game-update"), DurableFile.Json)!;
            if (plan is null || plan.InstanceId != instance.Id || plan.Revision != instance.Revision || plan.SourceHash != instance.SourceHash ||
                plan.Image != instance.Image || plan.ExpiresUtc < DateTimeOffset.UtcNow) throw new FormatException();
        }
        catch (Exception error) when (error is FormatException or CryptographicException or JsonException or ArgumentException)
        { throw new PanelException("UpgradeCheckRequired", "更新检查已过期或实例已变化，请重新检查更新。", 409); }
        var current = Read(instance);
        if (current.DepotManifest != plan.CurrentManifest) throw new PanelException("InstallationChanged", "实例安装版本已变化，请重新检查更新。", 409);
        var latest = await catalog.LatestAsync(cancellation);
        if (latest.DepotManifest == current.DepotManifest) throw new PanelException("NoUpdateAvailable", "当前已是最新版本，不需要更新。", 409);
        if (latest.DepotManifest != plan.TargetManifest || latest.BuildId != plan.TargetBuild)
            throw new PanelException("UpdateTargetChanged", "可更新版本已变化，请重新检查更新。", 409);
        return plan;
    }

    private static SteamInstallation Read(InstanceRecord instance)
    {
        var settings = EnvironmentFile.ParseRaw(File.ReadAllText(SafePaths.Within(instance.Root, "settings.env")));
        if ((settings.TryGetValue("TARGET_MANIFEST_ID", out var target) && !string.IsNullOrWhiteSpace(target)) ||
            (settings.TryGetValue("INSTALL_BETA_INSIDER", out var beta) && string.Equals(beta, "true", StringComparison.OrdinalIgnoreCase)) ||
            (settings.TryGetValue("SERVER_PLATFORM", out var platform) && !string.Equals(platform, "Linux", StringComparison.OrdinalIgnoreCase)))
            throw new PanelException("CustomInstallation", "此实例使用固定版本、测试版本或其他平台，暂不能自动升级。", 409);
        return SteamInstallation.Read(instance);
    }

    public static void VerifyInstallation(InstanceRecord instance, GameUpdatePlan plan)
    {
        VerifyContent(instance, plan);
        if (plan.TargetGameVersion is { } expected && !string.Equals(instance.GameBuild, expected, StringComparison.OrdinalIgnoreCase) &&
            !(instance.GameBuild?.StartsWith(expected + ".", StringComparison.OrdinalIgnoreCase) ?? false))
            throw new PanelException("GameBuildMismatch", "游戏实际运行版本不符合目标版本，请在任务页处理。", 409);
    }

    public static void VerifyContent(InstanceRecord instance, GameUpdatePlan plan)
    {
        if (SteamInstallation.Read(instance).DepotManifest != plan.TargetManifest)
            throw new PanelException("GameBuildMismatch", "安装结果不符合目标版本，已保留恢复点，请在任务页处理。", 409);
    }
}
