using System.Collections.Concurrent;
using System.Security.Cryptography;
using PalworldPanel.Server.Domain;

namespace PalworldPanel.Server.Infrastructure;

public sealed class PreparedDownloads : BackgroundService
{
    private sealed record Download(FileStream Stream, string User, string Source, string Name, DateTimeOffset Expires);
    private readonly ConcurrentDictionary<string, Download> downloads = new(StringComparer.Ordinal);
    public object Prepare(FileStream stream, string user, string source, string name)
    {
        Expire();
        if (downloads.Count >= 16) { stream.Dispose(); throw new PanelException("DownloadLimit", "下载准备过多，请稍后重试。", 429); }
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expiry = DateTimeOffset.UtcNow.AddMinutes(2);
        downloads[id] = new(stream, user, source, name, expiry);
        return new { downloadUrl = "/api/v1/downloads/" + id, expiresUtc = expiry };
    }
    public IResult Consume(string id, string user, string source)
    {
        Expire();
        if (!downloads.TryGetValue(id, out var download) || download.User != user || download.Source != source ||
            !downloads.TryRemove(id, out download)) throw new PanelException("DownloadExpired", "下载不存在、已使用或来源已变化。", 404);
        return Results.File(download.Stream, "application/octet-stream", download.Name);
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { while (!stoppingToken.IsCancellationRequested) { Expire(); await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); } }
        finally { foreach (var id in downloads.Keys) if (downloads.TryRemove(id, out var item)) item.Stream.Dispose(); }
    }
    private void Expire()
    { foreach (var item in downloads.Where(d => d.Value.Expires < DateTimeOffset.UtcNow)) if (downloads.TryRemove(item.Key, out var expired)) expired.Stream.Dispose(); }
}
