using PalworldPanel.Server.Domain;
using PalworldPanel.Server.Infrastructure;

namespace PalworldPanel.UnitTests;

public sealed class RuntimeMemoryTests
{
    [Fact]
    public void ReservationsIgnoreStoppedAllocationsAndFinishedTasksAndExcludeCurrentInstance()
    {
        InstanceRecord Instance(string id) => new(id, id, id, id, "palworld", "image", 1, 2, 3, "", "", new GameRules(id, MemoryMiB: 16384));
        TaskRecord Task(string id, string instance, string kind, string state) => new(id, instance, kind, state, "Preflight", "{}", "test", id, "hash", "date");
        var instances = new[] { Instance("stopped"), Instance("self"), Instance("queued"), Instance("running"), Instance("completed") };
        var tasks = new[] { Task("1", "self", "start", "Running"), Task("2", "queued", "create", "Queued"),
            Task("3", "running", "start", "Running"), Task("4", "queued", "start", "Queued"), Task("5", "completed", "start", "Succeeded") };
        Assert.Equal(32768, RuntimeMemory.PendingMiB(tasks, instances, "self"));
        Assert.Equal(49152, RuntimeMemory.PendingMiB(tasks, instances, null));
    }

    [Fact]
    public void AvailableMemoryIncludesReclaimableCacheInsteadOfOnlyMemFree()
    {
        var value = RuntimeMemory.Parse("MemTotal: 32768000 kB\nMemFree: 1000 kB\nMemAvailable: 24000000 kB\n");
        Assert.Equal(24000000L * 1024, value.AvailableBytes);
        Assert.Equal(value.TotalBytes, value.UsedBytes + value.AvailableBytes);
    }

    [Theory]
    [InlineData(28, 16, 4, 0, true)]
    [InlineData(28, 20, 4, 0, true)]
    [InlineData(28, 25, 4, 0, false)]
    [InlineData(28, 16, 4, 16, false)]
    [InlineData(20, 16, 4, 0, true)]
    [InlineData(3, 1, 4, 0, false)]
    public void AdmissionUsesActualAvailableMemoryAndPendingStarts(int free, int requested, int reserve, int pending, bool accepted)
    {
        void Check() => RuntimeMemory.Check((long)free << 30, requested * 1024, reserve * 1024, pending * 1024);
        if (accepted) Check();
        else Assert.Equal("ResourceBudgetExceeded", Assert.Throws<PanelException>(Check).Code);
    }

    [Theory]
    [InlineData("MemTotal: 100 kB")]
    [InlineData("MemTotal: 100 kB\nMemAvailable: 101 kB")]
    [InlineData("MemTotal: 0 kB\nMemAvailable: 0 kB")]
    [InlineData("MemTotal: 100 kB\nMemAvailable: -1 kB")]
    [InlineData("MemTotal: 100 kB\nMemAvailable: unknown kB")]
    [InlineData("MemTotal: 100 kB\nMemAvailable: 50 kB\nMemAvailable: 50 kB")]
    public void UnknownOrInvalidSamplesFailClosed(string text) =>
        Assert.Equal("MemorySampleUnavailable", Assert.Throws<PanelException>(() => RuntimeMemory.Parse(text)).Code);
}
