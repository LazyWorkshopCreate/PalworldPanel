using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PalworldPanel.Server.Domain;

namespace PalworldPanel.Server.Infrastructure;

public sealed class GameRestClient(SecretVault vault)
{
    private readonly HttpClient client = new(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
    { Timeout = TimeSpan.FromSeconds(10) };

    public async Task<JsonElement> ReadAsync(InstanceRecord instance, string endpoint, CancellationToken cancellation = default)
    {
        if (endpoint is not ("info" or "metrics" or "settings" or "players")) throw new InvalidOperationException("不支持的 REST 读取。");
        using var request = Create(instance, HttpMethod.Get, endpoint);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            throw new PanelException("GameUnauthorized", "游戏 API 凭据无效。", 409);
        if (!response.IsSuccessStatusCode) throw new PanelException("GameApiUnavailable", "游戏 API 暂不可用。", 409);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellation);
        using var limited = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellation)) != 0)
        {
            if (limited.Length + count > 1L << 20) throw new PanelException("GameResponseTooLarge", "游戏响应超限。", 409);
            limited.Write(buffer, 0, count);
        }
        using var document = JsonDocument.Parse(limited.ToArray());
        return document.RootElement.Clone();
    }

    public async Task SaveAsync(InstanceRecord instance, CancellationToken cancellation = default)
    {
        using var request = Create(instance, HttpMethod.Post, "save");
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
            if (!response.IsSuccessStatusCode) throw new PanelException("SaveFailed", "保存请求失败，未停止实例。", 409);
        }
        catch (TaskCanceledException) when (!cancellation.IsCancellationRequested)
        { throw new PanelException("SaveTimeout", "保存请求超时，结果未知；实例保持运行，未自动停止。", 409); }
        catch (HttpRequestException)
        { throw new PanelException("SaveUnavailable", "保存接口不可访问；实例未自动停止。", 409); }
    }

    public async Task AnnounceAsync(InstanceRecord instance, string message, CancellationToken cancellation = default)
    {
        using var request = Create(instance, HttpMethod.Post, "announce");
        request.Content = new StringContent(JsonSerializer.Serialize(new { message }), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
        if (!response.IsSuccessStatusCode) throw new PanelException("AnnounceFailed", "维护公告失败。", 409);
    }

    private HttpRequestMessage Create(InstanceRecord instance, HttpMethod method, string endpoint)
    {
        if (instance.RestPort is < 1024 or > 65535) throw new PanelException("InvalidPort", "游戏 API 端口无效。", 409);
        var request = new HttpRequestMessage(method, $"http://127.0.0.1:{instance.RestPort}/v1/api/{endpoint}");
        var password = vault.Open(instance.AppliedAdminCipher ?? instance.AdminCipher, instance.Id + ":admin");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:" + password)));
        return request;
    }
}
