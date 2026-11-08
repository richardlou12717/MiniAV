namespace MiniAV.Engines.ClamAV;

/// <summary>
/// Owns reader resources until every associated task is terminal. On join timeout,
/// ownership transfers to the completion continuation, never to an early using dispose.
/// This does not itself cancel a blocked read or guarantee eventual resource release.
/// </summary>
internal sealed class DeferredReaderCleanup
{
    private readonly IDisposable[] _resources;
    private readonly Task[] _readers;
    private int _disposed;
    private int _deferred;
    private readonly TaskCompletionSource<Exception?> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task<Exception?> CleanupCompletion => _completion.Task;

    internal DeferredReaderCleanup(IEnumerable<IDisposable> resources, params Task[] readers)
    {
        _resources = resources.ToArray();
        _readers = readers;
    }

    internal bool WasDeferred => Volatile.Read(ref _deferred) != 0;
    internal bool ResourcesDisposed => Volatile.Read(ref _disposed) != 0;

    internal async Task JoinOrDeferAsync(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        var combined = Task.WhenAll(_readers);
        try
        {
            await combined.WaitAsync(timeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            Interlocked.Exchange(ref _deferred, 1);
            // ExecuteSynchronously can run on this thread; never dispose until *all*
            // readers are terminal. Task.Exception also observes reader failures.
            _ = combined.ContinueWith(task =>
            {
                _ = task.Exception;
                DisposeResources();
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            throw new IOException("Native pipe readers failed to quiesce; cleanup deferred until task completion.");
        }
        catch (Exception) when (_readers.All(static reader => reader.IsCompleted))
        {
            DisposeResources();
            throw;
        }
        DisposeResources();
    }

    private void DisposeResources()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        var failures = new List<Exception>();
        foreach (var resource in _resources)
        {
            try { resource.Dispose(); }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }
        var error = failures.Count == 0 ? null :
            new AggregateException("Deferred reader resource disposal failed.", failures);
        if (error is not null)
            System.Diagnostics.Trace.TraceError(error.ToString());
        _completion.TrySetResult(error);
    }
}
