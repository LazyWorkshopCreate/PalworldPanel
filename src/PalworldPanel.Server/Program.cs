using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using PalworldPanel.Server.Domain;
using PalworldPanel.Server.Infrastructure;
using Serilog;
using PalworldPanel.Server.Application;
using PalworldPanel.Server.Workers;

if (args.Length == 2 && args[0] == "--quarantine-pending-tasks")
{
    var store = new SqliteStore(args[1]);
    foreach (var task in store.Tasks("Queued").Concat(store.Tasks("Running")))
        store.SetTask(task.Id, "NeedsAttention", task.Phase, "DatabaseRestored", message: "灾备恢复后的旧任务必须逐项核实，不自动重放。");
    Console.WriteLine("待执行及执行中任务已转为人工核实，未连接 Docker 或游戏。");
    return;
}
if (args.Length == 2 && args[0] == "--check-idle")
{
    var store = new SqliteStore(args[1]);
    if (store.Tasks("Queued").Concat(store.Tasks("Running")).Concat(store.Tasks("NeedsAttention")).Any())
        throw new InvalidOperationException("存在排队、运行或待处理任务，拒绝升级/卸载。请先等待或处理任务。");
    Console.WriteLine("没有排队、运行或待处理任务。");
    return;
}

if (args.Length == 3 && args[0] == "--decrypt-export")
{
    var passphrase = Environment.GetEnvironmentVariable("PANEL_EXPORT_PASSPHRASE")
        ?? throw new InvalidOperationException("请通过环境变量提供导出口令。");
    if (File.Exists(args[2]) || File.Exists(args[2] + ".partial")) throw new InvalidOperationException("目标已存在，拒绝覆盖。");
    SafePaths.RejectLinks(args[2]);
    try
    {
        await using (var input = File.OpenRead(args[1]))
        await using (var output = new FileStream(args[2] + ".partial", FileMode.CreateNew, FileAccess.Write))
        {
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(args[2] + ".partial", UnixFileMode.UserRead | UnixFileMode.UserWrite);
            await EncryptedArchive.DecryptAsync(input, output, passphrase);
            output.Flush(true);
        }
        File.Move(args[2] + ".partial", args[2]);
        DurableFile.FlushDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);
        Console.WriteLine("加密导出验证与解密完成，未输出凭据。");
    }
    catch { File.Delete(args[2] + ".partial"); throw; }
    return;
}

if (args.Length == 2 && args[0] == "--prepare-administrator")
{
    AdministratorSecurity.PrepareWebSetup(args[1]);
    Console.WriteLine("首次网站设置入口已准备，未生成管理员密码。");
    return;
}
if (args.Length == 2 && args[0] == "--initialize-administrator")
{
    var password = Environment.GetEnvironmentVariable("PANEL_INITIAL_ADMIN_PASSWORD")
        ?? throw new InvalidOperationException("通过环境变量提供初始化密码。");
    AdministratorSecurity.Initialize(args[1], "admin", password);
    Console.WriteLine("管理员初始化完成，未输出凭据。");
    return;
}
if (args.Length == 2 && args[0] == "--initialize-key")
{
    if (File.Exists(args[1])) throw new InvalidOperationException("密钥已经存在，拒绝覆盖。");
    DurableFile.Write(args[1], RandomNumberGenerator.GetBytes(32));
    Console.WriteLine("密钥初始化完成，未输出凭据。");
    return;
}

if (args.Length == 2 && args[0] == "--validate-config")
{
    _ = PanelOptions.Load(args[1]);
    Console.WriteLine("配置校验通过，未连接 Docker 或修改实例。");
    return;
}
var configPath = args.Length == 2 && args[0] == "--config" ? args[1] : Environment.GetEnvironmentVariable("PANEL_CONFIG_FILE")
    ?? throw new InvalidOperationException("PANEL_CONFIG_FILE 未配置，拒绝启动。");
var options = PanelOptions.Load(configPath);
using var executorLock = DiskLock.Acquire(Path.Combine(options.StateRoot, "executor.lock"));
var certificate = options.UsesHttp && !File.Exists(options.CertificateFile) ? null : X509CertificateLoader.LoadPkcs12FromFile(options.CertificateFile,
    File.ReadAllText(options.CertificatePasswordFile).Trim());
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args.Length == 2 && args[0] == "--config" ? [] : args,
    ContentRootPath = AppContext.BaseDirectory
});
builder.Host.UseWindowsService(service => service.ServiceName = "PalworldPanel");
builder.WebHost.ConfigureKestrel(server =>
{
    server.Limits.MaxRequestBodySize = 4L << 30;
    server.Listen(IPAddress.Parse(options.BindIp), options.Port, endpoint =>
    {
        if (!options.UsesHttp) endpoint.UseHttps(certificate!);
    });
    if (OperatingSystem.IsWindows())
        server.Listen(IPAddress.Loopback, options.Port, endpoint =>
        {
            if (!options.UsesHttp) endpoint.UseHttps(certificate!);
        });
});
builder.Services.AddSingleton(options);
builder.Services.AddSingleton(new SourceAccessPolicy(options.AllowedIps, allowLocalhost: OperatingSystem.IsWindows()));
builder.Services.AddSingleton(new SecretVault(options.KeyFile));
builder.Services.AddSingleton(new SqliteStore(Path.Combine(options.StateRoot, "panel.db")));
builder.Services.AddSingleton(new AdministratorSecurity(options.AdministratorFile, options.AllowWebSetup));
builder.Services.AddSingleton<ConfirmationTokens>();
builder.Services.AddSingleton<HeavyIoGate>();
builder.Services.AddSingleton<DockerBackend>();
builder.Services.AddSingleton<GameRestClient>();
builder.Services.AddHttpClient<SteamReleaseCatalog>().ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton<GameUpdateService>();
builder.Services.AddSingleton<HostMetrics>();
builder.Services.AddSingleton<InstanceService>();
builder.Services.AddSingleton<PublicDashboard>();
builder.Services.AddSingleton<BackupService>();
builder.Services.AddSingleton<DiscoveryService>();
builder.Services.AddSingleton<TaskRecoveryService>();
builder.Services.AddSingleton<BackupExportService>();
builder.Services.AddSingleton<PanelBackupService>();
builder.Services.AddSingleton<QuarantineService>();
builder.Services.AddSingleton<PreparedDownloads>();
builder.Services.AddHostedService(service => service.GetRequiredService<PreparedDownloads>());
builder.Services.AddHostedService<TaskWorker>();
builder.Services.AddHostedService<BackupScheduler>();
builder.Services.AddHostedService<MetadataMaintenance>();
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("PalworldPanel")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(options.StateRoot, "session-keys")));
if (certificate is not null) dataProtection.ProtectKeysWithCertificate(certificate);
else if (OperatingSystem.IsWindows()) dataProtection.ProtectKeysWithDpapi(protectToLocalMachine: true);
builder.Services.AddAuthentication("PanelCookie").AddCookie("PanelCookie", cookie =>
{
    cookie.Cookie.Name = options.UsesHttp ? "PalworldPanel" : "__Host-PalworldPanel";
    cookie.Cookie.HttpOnly = true;
    cookie.Cookie.SecurePolicy = options.UsesHttp ? CookieSecurePolicy.None : CookieSecurePolicy.Always;
    cookie.Cookie.SameSite = SameSiteMode.Strict;
    cookie.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    cookie.SlidingExpiration = true;
    cookie.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
    cookie.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization();
builder.Host.UseSerilog((_, configuration) => configuration.MinimumLevel.Warning()
    .WriteTo.File(Path.Combine(options.StateRoot, "logs", "panel-.log"), rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30, fileSizeLimitBytes: 10L << 20, rollOnFileSizeLimit: true));
var app = builder.Build();
app.Services.GetRequiredService<InstanceService>().MigrateSourceHashes();
app.Use(async (context, next) =>
{
    if (!app.Services.GetRequiredService<SourceAccessPolicy>().Allows(context.Connection.RemoteIpAddress))
    { context.Response.StatusCode = 403; await context.Response.WriteAsync("Forbidden"); return; }
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    context.Response.Headers["Cache-Control"] = "no-store";
    var styleNonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    context.Items["style-nonce"] = styleNonce;
    context.Response.Headers["Content-Security-Policy"] = BrowserSecurityHeaders.ContentSecurityPolicy(styleNonce, OperatingSystem.IsWindows(), context.Connection.RemoteIpAddress);
    try { await next(context); }
    catch (PanelException error)
    { await Results.Problem(statusCode: error.Status, title: error.Message, extensions: new Dictionary<string, object?> { ["code"] = error.Code }).ExecuteAsync(context); }
    catch (Exception error)
    {
        Log.Error("Request failed: {ExceptionType}; {StackTrace}", error.GetType().Name, error.StackTrace);
        await Results.Problem(statusCode: 500, title: "请求未完成，请查看任务状态。", extensions: new Dictionary<string, object?> { ["code"] = "InternalError" }).ExecuteAsync(context);
    }
});
app.UseAuthentication();
app.Use(async (context, next) =>
{
    if (context.Request.Method is not ("GET" or "HEAD" or "OPTIONS"))
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (origin != context.Request.Scheme + "://" + context.Request.Host)
        { context.Response.StatusCode = 403; return; }
        if ((context.Request.Path != "/api/v1/session" && context.Request.Path != "/api/v1/setup") || context.Request.Method != "POST")
        {
            if (context.User.Identity?.IsAuthenticated != true) { context.Response.StatusCode = 401; return; }
            var expected = context.User.FindFirstValue("csrf");
            var actual = context.Request.Headers["X-CSRF-Token"].ToString();
            if (expected is null || actual.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(actual), System.Text.Encoding.UTF8.GetBytes(expected)))
            { context.Response.StatusCode = 403; return; }
        }
    }
    await next(context);
});
app.UseAuthorization();
app.MapGet("/api/v1/setup", (AdministratorSecurity administrator) =>
    Results.Ok(new { required = administrator.SetupRequired, token = administrator.SetupToken })).AllowAnonymous();
app.MapPost("/api/v1/setup", (SetupRequest request, AdministratorSecurity administrator) =>
{
    administrator.CompleteWebSetup(request.Token, request.Password, request.Confirmation);
    return Results.NoContent();
}).AllowAnonymous();
app.MapGet("/api/v1/dashboard", async (PublicDashboard dashboard, CancellationToken ct) =>
    Results.Ok(await dashboard.ReadAsync(ct))).AllowAnonymous();
app.MapPost("/api/v1/session", async (HttpContext context, LoginRequest login, AdministratorSecurity administrator, SqliteStore store) =>
{
    var principal = administrator.Authenticate(login.UserName, login.Password,
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
    await context.SignInAsync("PanelCookie", principal);
    store.Audit(principal.Identity!.Name!, null, "login", "Succeeded");
    return Results.NoContent();
});
app.MapGet("/api/v1/session", (HttpContext context) => Results.Ok(new
{
    userName = context.User.Identity!.Name, csrfToken = context.User.FindFirstValue("csrf"),
    authenticatedAt = context.User.FindFirstValue("authenticatedAt")
})).RequireAuthorization();
app.MapDelete("/api/v1/session", async (HttpContext context) =>
{ await context.SignOutAsync("PanelCookie"); return Results.NoContent(); }).RequireAuthorization();
app.MapGet("/api/v1/tasks", (SqliteStore store) => Results.Ok(store.Tasks().Select(t => new
{ t.Id, t.InstanceId, t.Kind, t.State, t.Phase, t.CreatedUtc, t.SafeCode, t.RecoveryPoint, t.Message }))).RequireAuthorization();
app.MapFallback("/api/{**path}", () => Results.Problem(statusCode: 404, title: "接口不存在。"));
app.MapPanelEndpoints();
app.Use(async (context, next) =>
{
    if (context.Request.Path == "/index.html" && context.Request.Method is "GET" or "HEAD") await RenderIndex(context);
    else await next(context);
});
app.UseStaticFiles();
app.MapFallback(RenderIndex);
app.Run();

async Task RenderIndex(HttpContext context)
{
    var path = Path.Combine(app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"), "index.html");
    if (!File.Exists(path)) { context.Response.StatusCode = 404; return; }
    var html = (await File.ReadAllTextAsync(path, context.RequestAborted)).Replace("__STYLE_NONCE__", context.Items["style-nonce"]?.ToString(), StringComparison.Ordinal);
    context.Response.ContentType = "text/html; charset=utf-8";
    if (context.Request.Method != "HEAD") await context.Response.WriteAsync(html, context.RequestAborted);
}

public sealed record LoginRequest(string UserName, string Password);
public partial class Program;

public sealed record SetupRequest(string Token, string Password, string Confirmation);
