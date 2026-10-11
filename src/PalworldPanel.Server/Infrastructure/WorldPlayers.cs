using System.Text.Json;

namespace PalworldPanel.Server.Infrastructure;

public sealed record WorldPlayerView(string UserId, string? Name, int? Level, string? PlayerId);
public sealed record WorldRosterEntry(string? UserId, string? Name, int? Level, string? PlayerId, bool Online, bool? Banned);

public static class WorldPlayers
{
    public static async Task<(WorldPlayerView[] Players, string? Unavailable)> ReadOnlineAsync(
        Func<CancellationToken, Task<JsonElement>> read, CancellationToken cancellation)
    {
        try { return (Project(await read(cancellation)), null); }
        catch (Exception error) when (!cancellation.IsCancellationRequested && error is
            PalworldPanel.Server.Domain.PanelException or HttpRequestException or OperationCanceledException)
        { return ([], "暂时无法读取在线玩家。列表仍包含已经记录的玩家。"); }
    }

    public static WorldPlayerView[] Project(JsonElement root)
    {
        var list = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("players", out var players) ? players : root;
        if (list.ValueKind != JsonValueKind.Array) return [];
        var result = new List<WorldPlayerView>();
        foreach (var player in list.EnumerateArray())
        {
            var userId = player.TryGetProperty("userId", out var id) ? id.GetString() : null;
            if (!ServerBanList.IsUserId(userId)) continue;
            var name = player.TryGetProperty("name", out var shown) ? shown.GetString() : null;
            if (name is not null)
            {
                name = new string(name.Where(character => !char.IsControl(character)).Take(64).ToArray());
                if (name.Length == 0) name = null;
            }
            int? level = player.TryGetProperty("level", out var current) && current.TryGetInt32(out var number) ? number : null;
            var playerId = player.TryGetProperty("playerId", out var save) ? save.GetString() : null;
            if (!Guid.TryParseExact(playerId, "N", out var guid)) playerId = null;
            else playerId = guid.ToString("N");
            result.Add(new(userId!, name, level, playerId));
        }
        return result.ToArray();
    }

    public static WorldRosterEntry[] Merge(IEnumerable<WorldPlayerView> remembered, IEnumerable<WorldPlayerView> online,
        IEnumerable<string> bans, IEnumerable<string> saveIds)
    {
        var banned = new HashSet<string>(bans.Where(ServerBanList.IsUserId), StringComparer.Ordinal);
        var current = online.Where(player => ServerBanList.IsUserId(player.UserId)).ToDictionary(player => player.UserId, StringComparer.Ordinal);
        var history = remembered.Where(player => ServerBanList.IsUserId(player.UserId)).ToDictionary(player => player.UserId, StringComparer.Ordinal);
        var usedSaves = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<WorldRosterEntry>();
        foreach (var userId in current.Keys.Concat(history.Keys).Concat(banned).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            current.TryGetValue(userId, out var now);
            history.TryGetValue(userId, out var past);
            var source = now ?? past;
            var playerId = source?.PlayerId;
            if (playerId is not null) usedSaves.Add(playerId);
            rows.Add(new(userId, source?.Name, now?.Level ?? source?.Level, playerId, now is not null, banned.Contains(userId)));
        }
        foreach (var saveId in saveIds)
        {
            if (!Guid.TryParseExact(saveId, "N", out var guid)) continue;
            var id = guid.ToString("N");
            if (!usedSaves.Add(id)) continue;
            rows.Add(new(null, null, null, id, false, null));
        }
        return rows.ToArray();
    }
}
