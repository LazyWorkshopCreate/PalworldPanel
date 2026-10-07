using System.Net;
using System.Text;
using System.Text.Json;
using PalworldPanel.Server.Application;
using PalworldPanel.Server.Domain;
using PalworldPanel.Server.Infrastructure;

namespace PalworldPanel.UnitTests;

public sealed class GameUpdateTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "pp-update-test-" + Guid.NewGuid().ToString("N"));
    private const string Metadata = """{"data":{"2394010":{"depots":{"branches":{"public":{"buildid":"200","timeupdated":"1000"}},"2394012":{"manifests":{"public":{"gid":"222"}}}}}}} """;
    private const string News = """{"appnews":{"newsitems":[{"title":"v1.0.5: fixes","date":1001}]}}""";
    private static string Manifest(string manifest = "111", string build = "100", string flags = "4") => $$"""
        "AppState" { "appid" "2394010" "StateFlags" "{{flags}}" "buildid" "{{build}}" "UpdateResult" "0"
          "InstalledDepots" { "2394012" { "manifest" "{{manifest}}" } } }
        """;
    private InstanceRecord Instance()
    {
        Directory.CreateDirectory(Path.Combine(root, "data", "steamapps"));
        File.WriteAllText(Path.Combine(root, "settings.env"), "SERVER_PLATFORM=Linux\n");
        File.WriteAllText(Path.Combine(root, "data", "steamapps", "appmanifest_2394010.acf"), Manifest());
        return new("synthetic", "synthetic", root, "synthetic", "palworld", "approved@sha256:synthetic", 19211, 19312, 19415,
            "cipher", "cipher", new("synthetic"), GameBuild: "v1.0.4", SourceHash: "source");
    }
    private (GameUpdateService Service, SecretVault Vault) Service(string metadata = Metadata, string news = News)
    {
        Directory.CreateDirectory(root);
        var key = Path.Combine(root, "key");
        File.WriteAllBytes(key, new byte[32]);
        var vault = new SecretVault(key);
        return (new(new(new HttpClient(new Handler(metadata, news))), vault), vault);
    }
    [Fact]
    public async Task SameContentIsCurrentEvenWhenImageWritesOldBuildNumber()
    {
        var instance = Instance();
        File.WriteAllText(Path.Combine(root, "data", "steamapps", "appmanifest_2394010.acf"), Manifest("222", "1"));
        var result = await Service().Service.CheckAsync(instance, default);
        Assert.Equal("up-to-date", result.Status);
        Assert.Null(result.UpdateToken);
    }
    [Fact]
    public async Task TargetIsBoundToInstanceRevisionSourceAndInstallation()
    {
        var instance = Instance();
        var service = Service().Service;
        var check = await service.CheckAsync(instance, default);
        Assert.Equal("available", check.Status);
        Assert.Equal("v1.0.5", check.TargetVersion);
        var args = JsonSerializer.SerializeToElement(new { check.UpdateToken }, DurableFile.JsonCompact);
        Assert.Equal("222", (await service.ValidateAsync(instance, args, default)).TargetManifest);
        Assert.Equal("UpgradeCheckRequired", (await Assert.ThrowsAsync<PanelException>(() => service.ValidateAsync(instance with { Revision = 2 }, args, default))).Code);
        Assert.Equal("UpgradeCheckRequired", (await Assert.ThrowsAsync<PanelException>(() => service.ValidateAsync(instance with { SourceHash = "changed" }, args, default))).Code);
        File.WriteAllText(Path.Combine(root, "data", "steamapps", "appmanifest_2394010.acf"), Manifest("333"));
        Assert.Equal("InstallationChanged", (await Assert.ThrowsAsync<PanelException>(() => service.ValidateAsync(instance, args, default))).Code);
    }
    [Fact]
    public async Task ExpiredOrMissingCheckIsRejectedBeforeSideEffects()
    {
        var instance = Instance();
        var (service, vault) = Service();
        var plan = new GameUpdatePlan(instance.Id, 1, instance.SourceHash, instance.Image, "111", "222", "200", "v1.0.5", DateTimeOffset.UtcNow.AddSeconds(-1));
        var args = JsonSerializer.SerializeToElement(new { updateToken = vault.Seal(JsonSerializer.Serialize(plan, DurableFile.JsonCompact), instance.Id + ":game-update") });
        Assert.Equal("UpgradeCheckRequired", (await Assert.ThrowsAsync<PanelException>(() => service.ValidateAsync(instance, args, default))).Code);
        Assert.Equal("UpgradeCheckRequired", (await Assert.ThrowsAsync<PanelException>(() => service.ValidateAsync(instance, null, default))).Code);
    }
    [Fact]
    public async Task InvalidCatalogNeverMeansUpToDate()
    {
        var instance = Instance();
        Assert.Equal("UpdateCheckUnavailable", (await Assert.ThrowsAsync<PanelException>(() => Service("{}").Service.CheckAsync(instance, default))).Code);
    }
    [Fact]
    public async Task UnknownFriendlyVersionCannotStartUpdate()
    {
        var instance = Instance();
        Assert.Equal("UpdateVersionUnavailable", (await Assert.ThrowsAsync<PanelException>(() => Service(news: "{}").Service.CheckAsync(instance, default))).Code);
    }
    [Fact]
    public async Task ChangedTargetAndTamperedTokenAreRejected()
    {
        var instance = Instance();
        var check = await Service().Service.CheckAsync(instance, default);
        var args = JsonSerializer.SerializeToElement(new { check.UpdateToken }, DurableFile.JsonCompact);
        Assert.Equal("UpdateTargetChanged", (await Assert.ThrowsAsync<PanelException>(() => Service(Metadata.Replace("222", "333")).Service.ValidateAsync(instance, args, default))).Code);
        Assert.Equal("UpgradeCheckRequired", (await Assert.ThrowsAsync<PanelException>(() => Service().Service.ValidateAsync(instance,
            JsonSerializer.SerializeToElement(new { updateToken = "invalid" }), default))).Code);
    }
    [Theory]
    [InlineData("TARGET_MANIFEST_ID=123")]
    [InlineData("INSTALL_BETA_INSIDER=true")]
    [InlineData("SERVER_PLATFORM=Windows")]
    public async Task CustomInstallationsAreNotSilentlyConverted(string settings)
    {
        var instance = Instance();
        File.WriteAllText(Path.Combine(root, "settings.env"), settings);
        Assert.Equal("CustomInstallation", (await Assert.ThrowsAsync<PanelException>(() => Service().Service.CheckAsync(instance, default))).Code);
    }
    [Fact]
    public void VerificationRequiresBothInstalledContentAndRuntimeVersion()
    {
        var instance = Instance();
        var plan = new GameUpdatePlan(instance.Id, 1, instance.SourceHash, instance.Image, "111", "222", "200", "v1.0.5", DateTimeOffset.UtcNow.AddMinutes(10));
        Assert.Throws<PanelException>(() => GameUpdateService.VerifyContent(instance, plan));
        File.WriteAllText(Path.Combine(root, "data", "steamapps", "appmanifest_2394010.acf"), Manifest("222"));
        Assert.Throws<PanelException>(() => GameUpdateService.VerifyInstallation(instance, plan));
        GameUpdateService.VerifyInstallation(instance with { GameBuild = "v1.0.5.102999" }, plan);
        Assert.Throws<PanelException>(() => GameUpdateService.VerifyInstallation(instance with { GameBuild = "v1.0.50" }, plan));
    }
    [Theory]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("18446744073709551616", false)]
    [InlineData("-1", false)]
    public void NumericMetadataIsBounded(string value, bool valid) => Assert.Equal(valid, SteamReleaseCatalog.IsNumber(value));
    [Fact]
    public void IncompleteOrUnrelatedInstallationCannotBeUpgraded()
    {
        Assert.Throws<PanelException>(() => SteamInstallation.Parse(Manifest(flags: "6")));
        Assert.Throws<PanelException>(() => SteamInstallation.Parse(Manifest().Replace("2394010", "1623730")));
        Assert.Throws<PanelException>(() => SteamInstallation.Parse("\"AppState\" {"));
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    private sealed class Handler(string metadata, string news) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(request.RequestUri!.Host == "api.steamcmd.net" ? metadata : news, Encoding.UTF8, "application/json") });
    }
}
