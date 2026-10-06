namespace PalworldPanel.Server.Domain;

public sealed record GameRules(string Name, string Description = "", int MaxPlayers = 16,
    string DeathPenalty = "None", bool OfflinePenalty = false, double DeteriorationRate = 0,
    double AttackDamageRate = 1, double Cpu = 4, long MemoryMiB = 16384,
    Dictionary<string, string>? Additional = null)
{
    public bool HasSameValues(GameRules? other) => other is not null &&
        (this with { Additional = null }) == (other with { Additional = null }) &&
        (Additional?.Count ?? 0) == (other.Additional?.Count ?? 0) &&
        (Additional is null || Additional.All(pair => other.Additional is not null &&
            other.Additional.TryGetValue(pair.Key, out var value) && value == pair.Value));

    public void Validate()
    {
        GameSettingCatalog.Validate(Additional);
        if (Name is null || Description is null || Name.Length is < 1 or > 64 || Name.Any(char.IsControl) || Description.Length > 256 || Description.Any(char.IsControl) ||
            MaxPlayers is < 1 or > 32 || DeathPenalty is not ("None" or "Item" or "ItemAndEquipment" or "All") ||
            !double.IsFinite(DeteriorationRate) || DeteriorationRate is < 0 or > 100 ||
            !double.IsFinite(AttackDamageRate) || AttackDamageRate is < 0 or > 100 ||
            !double.IsFinite(Cpu) || Cpu <= 0 || MemoryMiB < 512)
            throw new PanelException("InvalidSetting", "配置字段不满足范围或类型要求。", 400);
    }
}

public sealed record InstanceRecord(string Id, string Name, string Root, string Project, string Service,
    string Image, int GamePort, int RestPort, int QueryPort, string AdminCipher, string GameCipher,
    GameRules Desired, GameRules? Applied = null, int Revision = 1, bool Writable = false, bool Owned = false,
    string? WorldGuid = null, string? SourceHash = null, string? ContainerId = null,
    string BackupTime = "05:00", int RetentionDays = 14, string DesiredPower = "stopped", string? GameBuild = null,
    string? QuarantinedUtc = null, string? AppliedAdminCipher = null, string? AppliedGameCipher = null, string? PurgedUtc = null,
    bool ResourceBudgetKnown = true);

public sealed record TaskRecord(string Id, string InstanceId, string Kind, string State, string Phase,
    string Payload, string User, string IdempotencyKey, string RequestHash, string CreatedUtc,
    string? SafeCode = null, string? RecoveryPoint = null, string? Message = null);
public sealed record BackupRecord(string Id, string InstanceId, string Path, string Sha256,
    string CreatedUtc, long Bytes, string? WorldGuid, string? GameBuild, string Image,
    bool Protected, bool ContainsInstallation, string Purpose);
