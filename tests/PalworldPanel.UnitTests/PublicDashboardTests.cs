using System.Text.Json;
using PalworldPanel.Server.Application;
using PalworldPanel.Server.Infrastructure;

namespace PalworldPanel.UnitTests;

public sealed class PublicDashboardTests
{
    [Fact]
    public void PublicProjectionDropsAllManagementAndSensitiveFields()
    {
        using var source = JsonDocument.Parse("""{"container":"running","players":3,"fps":45,"stale":false,"root":"synthetic-private","adminCipher":"synthetic-secret","serverPassword":"synthetic-secret","publishedUdp":[{"address":"synthetic-private"}],"desired":{"name":"synthetic-private"},"gameApi":"unauthorized"}""");
        var projection = PublicDashboard.Project(source.RootElement);
        Assert.Equal("running", projection.Container);
        Assert.Equal(3, projection.Players);
        Assert.False(projection.Stale);
        var output = JsonSerializer.Serialize(projection, DurableFile.Json);
        Assert.DoesNotContain("synthetic-", output);
        Assert.DoesNotContain("gameApi", output);
        Assert.DoesNotContain("desired", output);
    }
    [Fact]
    public void MissingObservationsRemainUnknownAndStale()
    {
        using var source = JsonDocument.Parse("{}");
        var result = PublicDashboard.Project(source.RootElement);
        Assert.Equal("unknown", result.Container);
        Assert.Null(result.Players);
        Assert.Null(result.Cpu);
        Assert.True(result.Stale);
    }
}
