using PalworldPanel.Server.Domain;
using PalworldPanel.Server.Infrastructure;

namespace PalworldPanel.UnitTests;

public sealed class WindowsDeploymentTests
{
    [Theory]
    [InlineData("/run/desktop/mnt/host/d/Panel/instances/a/data", "D:\\Panel\\instances\\a\\data")]
    [InlineData("/host_mnt/c/Panel/instances/a/data", "C:\\Panel\\instances\\a\\data")]
    [InlineData("D:/Panel/instances/a/data", "D:\\Panel\\instances\\a\\data")]
    [InlineData("D:\\Panel\\instances\\a\\data", "D:\\Panel\\instances\\a\\data")]
    public void DockerDesktopBindPathsMapToWindowsDrive(string source, string expected) =>
        Assert.Equal(expected, DockerPaths.WindowsHostPath(source));

    [Theory]
    [InlineData("/var/lib/docker/volumes/other")]
    [InlineData("//server/share/data")]
    [InlineData("D:/Panel/../other")]
    [InlineData("D:/Panel/data:secret")]
    [InlineData("relative/data")]
    public void UnknownAndUnsafeMountPathsStayUnavailable(string source) => Assert.Null(DockerPaths.WindowsHostPath(source));

    [Fact]
    public void WindowsHostMemorySampleUsesPhysicalMemory()
    {
        if (!OperatingSystem.IsWindows()) return;
        var value = new HostMetrics().Sample();
        Assert.True(value.TotalMemoryBytes > 0);
        Assert.True(value.AvailableMemoryBytes >= 0);
        Assert.Equal(value.TotalMemoryBytes, value.UsedMemoryBytes + value.AvailableMemoryBytes);
        Assert.True(value.CpuCount > 0);
    }

    [Theory]
    [InlineData("tcp://127.0.0.1:2375")]
    [InlineData("tcp://remote:2376")]
    [InlineData("npipe:////./pipe/docker_engine")]
    public void RemoteOrUnspecifiedWindowsDaemonIsRejected(string endpoint)
    {
        var options = new PanelOptions("192.168.1.2",18080,["192.168.1.3"],"unused",["unused"],"unused",
            "unused","unused","unused","unused","image",["image"],DockerEndpoint: endpoint);
        Assert.Throws<PanelException>(options.Validate);
    }
}
