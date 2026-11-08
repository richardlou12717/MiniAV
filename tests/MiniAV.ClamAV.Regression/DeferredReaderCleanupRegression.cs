using MiniAV.Engines.ClamAV;

// Pure lifetime fixture: no child process is launched.
internal static class DeferredReaderCleanupRegression
{
    private sealed class TrackedResource : IDisposable
    {
        internal int Disposals;
        public void Dispose() => Interlocked.Increment(ref Disposals);
    }

    private sealed class FaultResource : IDisposable
    {
        internal int Calls;
        public void Dispose()
        {
            Interlocked.Increment(ref Calls);
            throw new InvalidOperationException("INJECTED_DEFERRED_DISPOSE_FAILURE");
        }
    }

    internal static async Task RunAsync()
    {
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tracked = new TrackedResource();
        var owner = new DeferredReaderCleanup(new[] { tracked }, first.Task, second.Task);
        try
        {
            await owner.JoinOrDeferAsync(TimeSpan.FromMilliseconds(100));
            throw new Exception("Forced unfinished reader join incorrectly passed.");
        }
        catch (IOException ex) when (ex.Message.Contains("deferred", StringComparison.Ordinal)) { }

        if (!owner.WasDeferred || owner.ResourcesDisposed || tracked.Disposals != 0)
            throw new Exception("Resource disposed while both reader tasks were unfinished.");
        first.SetResult();
        await Task.Delay(50);
        if (owner.ResourcesDisposed || tracked.Disposals != 0)
            throw new Exception("Resource disposed while second reader was unfinished.");
        second.SetResult();
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!owner.ResourcesDisposed)
            await Task.Delay(10, guard.Token);
        if (tracked.Disposals != 1)
            throw new Exception($"Expected exactly one deferred disposal, observed {tracked.Disposals}.");
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var faulty = new FaultResource();
        var healthy = new TrackedResource();
        var failingOwner = new DeferredReaderCleanup(
            new IDisposable[] { faulty, healthy }, held.Task);
        try
        {
            await failingOwner.JoinOrDeferAsync(TimeSpan.FromMilliseconds(100));
            throw new Exception("Forced fault join unexpectedly succeeded.");
        }
        catch (IOException ex) when (ex.Message.Contains("deferred", StringComparison.Ordinal)) { }
        if (failingOwner.CleanupCompletion.IsCompleted || faulty.Calls != 0 || healthy.Disposals != 0)
            throw new Exception("Cleanup began while delayed reader remained incomplete.");
        held.SetResult();
        var cleanupError = await failingOwner.CleanupCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        if (cleanupError is not AggregateException aggregate ||
            aggregate.InnerExceptions.Count != 1 ||
            aggregate.InnerExceptions[0].Message != "INJECTED_DEFERRED_DISPOSE_FAILURE" ||
            faulty.Calls != 1 || healthy.Disposals != 1)
            throw new Exception("Deferred disposal error was not observable or remaining resources were skipped.");
        Console.WriteLine("M3.2 deferred disposal error observability passed");
        Console.WriteLine("M3.2 deferred reader ownership and exactly-once disposal passed");
    }
}
