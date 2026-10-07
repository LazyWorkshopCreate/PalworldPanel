using System.Text.Json;

namespace PalworldPanel.Server.Domain;

public sealed record InstanceActionRule(string Label, string Description, string[]? States, bool Writable, bool Healthy, bool Owned, bool ContainerRequired);

public static class InstanceActionPolicy
{
    public static readonly IReadOnlyDictionary<string, InstanceActionRule> Rules = JsonSerializer.Deserialize<Dictionary<string, InstanceActionRule>>(
        typeof(InstanceActionPolicy).Assembly.GetManifestResourceStream("instance-actions.json")!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    public static string? DisabledReason(InstanceRecord instance, string action, string container, string gameApi,
        bool stale = false, bool locked = false, DateTimeOffset? now = null, bool containerPresent = true)
    {
        if (!Rules.TryGetValue(action, out var rule)) return null;
        if (locked) return "实例有排队、执行中或待处理任务，请先查看任务。";
        if (action == "unmanage") return instance.QuarantinedUtc is not null && instance.PurgedUtc is null ? "请先撤销隔离或完成永久清空。" : null;
        if (instance.PurgedUtc is not null) return "实例数据已永久清空。";
        if (rule.Owned && !instance.Owned) return "仅面板创建的实例支持此操作。";
        if (action is "undo-quarantine" or "finalize-purge")
        {
            if (instance.QuarantinedUtc is null) return "实例未处于隔离状态。";
            if (action == "finalize-purge" && (now ?? DateTimeOffset.UtcNow) - DateTimeOffset.Parse(instance.QuarantinedUtc) < TimeSpan.FromDays(7))
                return "隔离数据必须保留至少 7 天。";
            return null;
        }
        if (instance.QuarantinedUtc is not null) return "实例已隔离，请先撤销隔离清理。";
        if (action == "adopt" && instance.Writable) return "实例已启用写管理。";
        if (rule.Writable && !instance.Writable) return "实例处于只读管理模式。";
        if (action is "clone" or "adopt" && !instance.Owned && instance.SourceHash is null) return "实例配置尚未核实。";
        if (action == "upgrade" && HasPendingDraft(instance)) return "请先应用参数与密码草稿。";
        if (rule.States is null) return null;
        if (stale || container == "unknown") return "运行状态未核实，请刷新后重试。";
        if (!rule.States.Contains(container)) return action == "start" && container == "running" ? "实例已经运行。" : "当前容器状态不支持此操作。";
        if (rule.ContainerRequired && !containerPresent) return "实例容器不存在，无需执行此操作。";
        if (rule.Healthy && container == "running" && gameApi != "healthy") return "游戏接口不可用，无法安全保存；需要停服时可选择强制停止。";
        return null;
    }

    public static bool HasPendingDraft(InstanceRecord i) => !i.Desired.HasSameValues(i.Applied) ||
        (i.AppliedAdminCipher is not null && i.AppliedAdminCipher != i.AdminCipher) ||
        (i.AppliedGameCipher is not null && i.AppliedGameCipher != i.GameCipher);
}
