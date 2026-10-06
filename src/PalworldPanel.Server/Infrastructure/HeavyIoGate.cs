namespace PalworldPanel.Server.Infrastructure;

public sealed class HeavyIoGate
{
    public SemaphoreSlim Semaphore { get; } = new(1);
}
