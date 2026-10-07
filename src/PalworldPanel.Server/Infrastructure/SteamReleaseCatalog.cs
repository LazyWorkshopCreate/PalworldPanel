using System.Text.Json;
using System.Text.RegularExpressions;
using PalworldPanel.Server.Domain;

namespace PalworldPanel.Server.Infrastructure;

public sealed record SteamRelease(string BuildId, string DepotManifest, string? GameVersion);

public sealed class SteamReleaseCatalog(HttpClient http)
{
    public async Task<SteamRelease> LatestAsync(CancellationToken cancellation)
    {
        try
        {
            using var metadata = await ReadAsync("https://api.steamcmd.net/v1/info/2394010", cancellation);
            var app = metadata.RootElement.GetProperty("data").GetProperty("2394010");
            var branch = app.GetProperty("depots").GetProperty("branches").GetProperty("public");
            var build = branch.GetProperty("buildid").GetString()!;
            var manifest = app.GetProperty("depots").GetProperty("2394012").GetProperty("manifests").GetProperty("public").GetProperty("gid").GetString()!;
            if (!IsNumber(build) || !IsNumber(manifest)) throw new InvalidOperationException();
            string? version = null;
            if (long.TryParse(branch.GetProperty("timeupdated").GetString(), out var updated))
            {
                try
                {
                    using var news = await ReadAsync("https://api.steampowered.com/ISteamNews/GetNewsForApp/v2/?appid=1623730&count=30&maxlength=1&feeds=steam_community_announcements", cancellation);
                    version = NewsVersion(news.RootElement, updated);
                }
                catch (Exception error) when (error is HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException or TaskCanceledException && !cancellation.IsCancellationRequested) { }
            }
            return new(build, manifest, version);
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException or TaskCanceledException && !cancellation.IsCancellationRequested)
        { throw new PanelException("UpdateCheckUnavailable", "暂时无法检查更新，请稍后重试。", 503); }
    }

    private async Task<JsonDocument> ReadAsync(string url, CancellationToken cancellation)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        limit.CancelAfter(TimeSpan.FromSeconds(15));
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, limit.Token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 1024 * 1024) throw new JsonException();
        await using var stream = await response.Content.ReadAsStreamAsync(limit.Token);
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, limit.Token)) > 0)
        {
            if (bytes.Length + count > 1024 * 1024) throw new JsonException();
            bytes.Write(buffer, 0, count);
        }
        return JsonDocument.Parse(bytes.ToArray());
    }

    public static string? NewsVersion(JsonElement news, long updated) => news.GetProperty("appnews").GetProperty("newsitems").EnumerateArray()
        .Select(item => new { Title = item.GetProperty("title").GetString() ?? "", Distance = Math.Abs(item.GetProperty("date").GetInt64() - updated) })
        .Where(item => item.Distance <= 6 * 3600)
        .OrderBy(item => item.Distance)
        .Select(item => Regex.Match(item.Title, @"(?i)\bv?(\d+\.\d+\.\d+(?:\.\d+)?)\b"))
        .Where(match => match.Success).Select(match => "v" + match.Groups[1].Value).FirstOrDefault();

    public static bool IsNumber(string? value) => value is { Length: > 0 and <= 20 } && value.All(char.IsAsciiDigit) && ulong.TryParse(value, out var number) && number > 0;
}
