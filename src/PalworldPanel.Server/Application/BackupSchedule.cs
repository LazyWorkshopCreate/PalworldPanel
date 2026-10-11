using PalworldPanel.Server.Domain;

namespace PalworldPanel.Server.Application;

public static class BackupSchedule
{
    public const int MaxAttempts = 3;

    public static string? NextKey(string instanceId, string day, IEnumerable<TaskRecord> existing)
    {
        var prefix = "schedule-" + instanceId + "-" + day;
        var attempts = existing.Where(task => IsAttempt(task, prefix)).ToArray();
        if (attempts.Any(task => task.State != "Failed" || task.Phase != "Preflight")) return null;
        var next = 1;
        foreach (var task in attempts)
            next = Math.Max(next, AttemptNumber(task.IdempotencyKey, prefix) + 1);
        if (attempts.Length >= MaxAttempts || next > MaxAttempts) return null;
        return next == 1 ? prefix : prefix + "-retry-" + next;
    }

    private static bool IsAttempt(TaskRecord task, string prefix) =>
        task.Kind == "backup" && task.User == "scheduler" && AttemptNumber(task.IdempotencyKey, prefix) > 0;

    private static int AttemptNumber(string key, string prefix)
    {
        if (key == prefix) return 1;
        var marker = prefix + "-retry-";
        return key.StartsWith(marker, StringComparison.Ordinal) &&
            int.TryParse(key[marker.Length..], System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var attempt) && attempt is >= 2 and <= MaxAttempts
            ? attempt : 0;
    }
}
