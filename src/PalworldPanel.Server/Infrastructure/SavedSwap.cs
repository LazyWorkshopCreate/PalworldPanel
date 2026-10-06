using System.Text.Json;
using PalworldPanel.Server.Domain;

namespace PalworldPanel.Server.Infrastructure;

public sealed record SwapJournal(string TaskId, string Phase, string Saved, string Staging, string Old, bool Accepted = false);

public sealed class SavedSwap(string parent, string taskId, string directoryName = "Saved")
{
    private readonly string saved = SafePaths.Within(parent, directoryName);
    private readonly string staging = SafePaths.Within(parent, directoryName + ".staging." + taskId);
    private readonly string old = SafePaths.Within(parent, directoryName + ".previous." + taskId);
    private readonly string journalFile = SafePaths.Within(parent, "swap." + taskId + ".json");
    private readonly string failed = SafePaths.Within(parent, directoryName + ".failed." + taskId);
    public string Staging => staging;
    public string Old => old;
    public string JournalFile => journalFile;

    public void Commit(Action<string>? checkpoint = null)
    {
        if (!Directory.Exists(saved) || !Directory.Exists(staging) || Directory.Exists(old))
            throw new PanelException("SwapPrecondition", "文件交换前置条件不满足。", 409);
        Write("SwapPrepared");
        checkpoint?.Invoke("SwapPrepared");
        Directory.Move(saved, old);
        DurableFile.FlushDirectory(parent);
        checkpoint?.Invoke("OldRenameBeforeJournal");
        Write("OldMoved");
        checkpoint?.Invoke("OldMoved");
        Directory.Move(staging, saved);
        DurableFile.FlushDirectory(parent);
        checkpoint?.Invoke("NewRenameBeforeJournal");
        Write("NewInstalled");
        checkpoint?.Invoke("NewInstalled");
    }

    public string InspectRecovery()
    {
        if (!File.Exists(journalFile)) return "NoJournal";
        var journal = JsonSerializer.Deserialize<SwapJournal>(File.ReadAllText(journalFile), DurableFile.Json)
            ?? throw new PanelException("InvalidJournal", "事务记录不可读取。", 503);
        if (journal.TaskId != taskId || journal.Saved != saved || journal.Staging != staging || journal.Old != old)
            throw new PanelException("InvalidJournal", "事务路径归属校验失败。", 503);
        SafePaths.RejectLinks(saved);
        SafePaths.RejectLinks(old);
        SafePaths.RejectLinks(staging);
        SafePaths.RejectLinks(failed);
        if (journal.Accepted) return "Accepted";
        if (journal.Phase == "RollbackPrepared")
        {
            if (Directory.Exists(old) && !Directory.Exists(saved) && Directory.Exists(failed)) return "RollbackOldReady";
            if (!Directory.Exists(old) && Directory.Exists(saved) && Directory.Exists(failed)) return "RollbackRestored";
        }
        if (Directory.Exists(old) && !Directory.Exists(saved) && Directory.Exists(staging)) return "OldMoved";
        if (Directory.Exists(old) && Directory.Exists(saved) && !Directory.Exists(staging)) return "NewInstalled";
        if (!Directory.Exists(old) && Directory.Exists(saved) && Directory.Exists(staging)) return "SwapPrepared";
        throw new PanelException("NeedsAttention", "事务文件状态不一致，禁止自动启动。", 409);
    }

    public void ResumeCommit(Action<string>? checkpoint = null)
    {
        var state = InspectRecovery();
        if (state == "NoJournal" || state == "SwapPrepared") { Commit(checkpoint); return; }
        if (state == "NewInstalled") return;
        if (state != "OldMoved") throw new PanelException("NeedsAttention", "当前交换不能继续，保留现场。", 409);
        Directory.Move(staging, saved);
        DurableFile.FlushDirectory(parent);
        Write("NewInstalled");
        checkpoint?.Invoke("NewInstalled");
    }

    public void Rollback(Action<string>? checkpoint = null)
    {
        var state = InspectRecovery();
        if (state == "Accepted")
        {
            var journal = JsonSerializer.Deserialize<SwapJournal>(File.ReadAllText(journalFile), DurableFile.Json)!;
            if (journal.Phase == "RolledBack") return;
            throw new PanelException("NeedsAttention", "已接受的新世界不能使用旧事务回退。", 409);
        }
        if (state == "SwapPrepared") { Write("RolledBack", true); return; }
        if (state == "RollbackRestored") { Write("RolledBack", true); return; }
        if (state is not ("OldMoved" or "NewInstalled" or "RollbackOldReady")) throw new PanelException("NeedsAttention", "无法安全回退。", 409);
        if (state == "NewInstalled")
        {
            if (Directory.Exists(failed)) throw new PanelException("NeedsAttention", "失败现场已存在，禁止覆盖。", 409);
            Write("RollbackPrepared");
            Directory.Move(saved, failed);
            DurableFile.FlushDirectory(parent);
            checkpoint?.Invoke("FailedRenameBeforeJournal");
        }
        Directory.Move(old, saved);
        DurableFile.FlushDirectory(parent);
        checkpoint?.Invoke("OriginalRestoreBeforeJournal");
        Write("RolledBack", true);
    }

    public void Accept()
    {
        if (InspectRecovery() != "NewInstalled") throw new PanelException("NeedsAttention", "未完成交换，不能接受。", 409);
        Write("Accepted", true);
    }
    private void Write(string phase, bool accepted = false) => DurableFile.WriteJson(journalFile,
        new SwapJournal(taskId, phase, saved, staging, old, accepted));
}
