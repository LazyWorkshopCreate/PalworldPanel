using System.Text.Json;
using System.Text.RegularExpressions;
using PalworldPanel.Server.Domain;
using PalworldPanel.Server.Infrastructure;

namespace PalworldPanel.Server.Application;

public sealed record WorldLoginRequest(string? WorldId, string? Password);
public sealed record WorldSettingView(string Key, string Label, string? Applied, string? Observed);
public static class WorldReadOnlyEndpoints
{
    public static WorldSettingView[] Settings(GameRules rules, JsonElement? observed) => GameSettingCatalog.Definitions
        .Where(d => !d.Secret && d.Editable && d.Key is not ("BanListURL" or "PublicIP" or "PublicPort"))
        .Select(d => new WorldSettingView(d.Key, d.Label, AppliedValue(d, rules), ObservedValue(d.Key, observed))).ToArray();
    private static string? AppliedValue(GameSettingDefinition d, GameRules r) => d.RuleKey switch
    {
        "name" => r.Name, "description" => r.Description, "maxPlayers" => r.MaxPlayers.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "deathPenalty" => r.DeathPenalty, "offlinePenalty" => r.OfflinePenalty.ToString(),
        "deteriorationRate" => r.DeteriorationRate.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "attackDamageRate" => r.AttackDamageRate.ToString(System.Globalization.CultureInfo.InvariantCulture),
        null => r.Additional?.GetValueOrDefault(d.Key), _ => null
    };
    private static string? ObservedValue(string key, JsonElement? value)
    {
        if (value is not { ValueKind: JsonValueKind.Object } element || !element.TryGetProperty(key, out var field)) return null;
        if (field.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False) return field.ToString();
        // The real game returns list settings as JSON arrays, including an empty deny list.
        if (field.ValueKind == JsonValueKind.Array && GameSettingCatalog.Definitions.Any(d => d.Key == key && d.Type == "list") &&
            field.EnumerateArray().All(item => item.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False))
            return "(" + string.Join(',', field.EnumerateArray().Select(item => item.ToString())) + ")";
        return null;
    }
    public static string CleanLogs(string text, InstanceRecord i, SecretVault vault)
    {
        text = LogRedactor.Clean(LogRedactor.LatestFirst(text), vault.Open(i.AdminCipher, i.Id + ":admin"), vault.Open(i.GameCipher, i.Id + ":game"),
            vault.Open(i.AppliedAdminCipher ?? i.AdminCipher, i.Id + ":admin"), vault.Open(WorldReadOnlySecurity.AppliedCipher(i), i.Id + ":game"));
        text = text.Replace(i.Root, "[redacted path]", StringComparison.OrdinalIgnoreCase);
        text = Regex.Replace(text, @"(?:[A-Za-z]:[\\/]|(?<![A-Za-z0-9:])/(?!/))[^\s""']+", "[redacted path]");
        return text.Length > 65536 ? text[..65536] : text;
    }
    public static void MapWorldReadOnlyEndpoints(this WebApplication app)
    {
        var api = app.MapGroup(WorldReadOnlySecurity.Path).AllowAnonymous();
        api.MapPost("/session", (WorldLoginRequest request, HttpContext context, WorldReadOnlySecurity security, PanelOptions options) =>
        {
            var token = security.Login(request.WorldId, request.Password, context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
            security.Logout(context.Request.Cookies[WorldReadOnlySecurity.Cookie]);
            context.Response.Cookies.Append(WorldReadOnlySecurity.Cookie, token, new CookieOptions { HttpOnly = true, Secure = !options.UsesHttp, SameSite = SameSiteMode.Strict, Path = WorldReadOnlySecurity.Path, MaxAge = TimeSpan.FromMinutes(30) });
            return Results.Ok(new { worldId = WorldReadOnlySecurity.Normalize(request.WorldId) });
        });
        api.MapDelete("/session", (HttpContext context, WorldReadOnlySecurity security) =>
        {
            security.Logout(context.Request.Cookies[WorldReadOnlySecurity.Cookie]);
            context.Response.Cookies.Delete(WorldReadOnlySecurity.Cookie, new CookieOptions { Path = WorldReadOnlySecurity.Path });
            return Results.NoContent();
        });
        api.MapGet("/{worldId}", async (string worldId, HttpContext context, WorldReadOnlySecurity security, InstanceService service, GameRestClient game, CancellationToken ct) =>
        {
            var i = security.Require(context.Request.Cookies[WorldReadOnlySecurity.Cookie], worldId);
            service.CheckRoot(i);
            JsonElement? observed = null;
            string? unavailable = null;
            try { observed = await game.ReadAsync(i, "settings", ct); }
            catch (Exception e) when (!ct.IsCancellationRequested && e is HttpRequestException or TaskCanceledException or PanelException) { unavailable = "运行设置不可用，已应用基准不代表实时观测。"; }
            PublicObservation status;
            try { status = PublicDashboard.Project(JsonSerializer.SerializeToElement(await service.ObserveAsync(i, ct), DurableFile.Json)); }
            catch (Exception e) when (!ct.IsCancellationRequested && e is HttpRequestException or TaskCanceledException or PanelException) { status = new(); }
            ct.ThrowIfCancellationRequested();
            security.Require(context.Request.Cookies[WorldReadOnlySecurity.Cookie], worldId);
            return Results.Ok(new { name = i.Applied!.Name, worldId = WorldReadOnlySecurity.Normalize(i.WorldGuid), i.GameBuild, i.GamePort, status,
                settings = Settings(i.Applied, observed), settingsUnavailable = unavailable, updatedUtc = DateTimeOffset.UtcNow,
                backups = service.Store.Backups(i.Id).Where(b => WorldReadOnlySecurity.Normalize(b.WorldGuid) == WorldReadOnlySecurity.Normalize(i.WorldGuid))
                    .Select(b => new { b.Id, b.CreatedUtc, b.Bytes, b.GameBuild }) });
        });
        api.MapGet("/{worldId}/logs", async (string worldId, HttpContext context, WorldReadOnlySecurity security, InstanceService service, DockerBackend docker, SecretVault vault, CancellationToken ct) =>
        {
            var i = security.Require(context.Request.Cookies[WorldReadOnlySecurity.Cookie], worldId);
            service.CheckRoot(i);
            var container = await docker.ContainerIdAsync(i, ct);
            var text = "容器不存在。";
            if (container is not null)
            {
                var result = await docker.RunAsync(["logs", "--tail", "200", "--timestamps", container], TimeSpan.FromSeconds(10), cancellation: ct, captureLogErrors: true);
                if (result.ExitCode != 0) throw new PanelException("WorldLogsUnavailable", "日志暂时不可用，请稍后重试。", 503);
                text = CleanLogs(result.Output, i, vault);
            }
            security.Require(context.Request.Cookies[WorldReadOnlySecurity.Cookie], worldId);
            return Results.Ok(new { text, updatedUtc = DateTimeOffset.UtcNow });
        });
        api.MapGet("/{worldId}/backups/{backupId}", async (string worldId, string backupId, HttpContext context, WorldReadOnlySecurity security, WorldSaveExport exports, CancellationToken ct) =>
        {
            var i = security.Require(context.Request.Cookies[WorldReadOnlySecurity.Cookie], worldId);
            var stream = await exports.ExportAsync(i, backupId, ct);
            try { security.Require(context.Request.Cookies[WorldReadOnlySecurity.Cookie], worldId); }
            catch { await stream.DisposeAsync(); throw; }
            return Results.File(stream, "application/zip", "palworld-world-" + WorldReadOnlySecurity.Normalize(i.WorldGuid) + "-" + backupId + ".zip");
        });
    }
}
