namespace PalworldPanel.Server.Infrastructure;

public static class StartupGate
{
    public static async Task WaitAsync(IHostApplicationLifetime lifetime, CancellationToken cancellation)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var ready = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        using var cancelled = cancellation.Register(() => started.TrySetCanceled(cancellation));
        await started.Task;
    }
}
