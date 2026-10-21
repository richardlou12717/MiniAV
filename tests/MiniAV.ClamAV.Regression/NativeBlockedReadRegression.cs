using MiniAV.Engines.ClamAV;

// M3.3: real pending kernel pipe reads, not a synthetic task-completion gate.
// All children are in-process safe pipe handles; no production runner changes.
internal static class NativeBlockedReadRegression
{

    // Real blocked read coupled to DeferredReaderCleanup: the owner must keep
    // the pipe open after a bounded join fails, and release it only after EOF.
    internal static async Task RunDeferredOwnerAsync()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var pipe = await WindowsOverlappedOutputPipe.CreateAsync()
            .WaitAsync(TimeSpan.FromSeconds(12));
        bool transferred = false;
        try
        {
            var pending = pipe.Parent.ReadAsync(new byte[128].AsMemory()).AsTask();
            await Task.Delay(150);
            if (pending.IsCompleted)
                throw new Exception("Real pipe read was not pending before deferred join.");
            var owner = new DeferredReaderCleanup(new IDisposable[] { pipe }, pending);
            transferred = true;
            try
            {
                await owner.JoinOrDeferAsync(TimeSpan.FromMilliseconds(100));
                throw new Exception("Pending kernel read unexpectedly joined.");
            }
            catch (IOException ex) when (ex.Message.Contains("deferred", StringComparison.Ordinal)) { }
            if (!owner.WasDeferred || owner.ResourcesDisposed || owner.CleanupCompletion.IsCompleted)
                throw new Exception("Pipe ownership was released while kernel read was pending.");
            // The writer remains open until this point, demonstrating deferred ownership.
            pipe.CloseChildWriter();
            if (await pending.WaitAsync(TimeSpan.FromSeconds(5)) != 0)
                throw new Exception("Expected EOF after releasing idle writer.");
            var cleanup = await owner.CleanupCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            if (cleanup is not null || !owner.ResourcesDisposed)
                throw new Exception("Deferred owner did not release real pipe cleanly.");
            Console.WriteLine("M3.3 real blocked-read deferred ownership and eventual disposal passed");
        }
        finally
        {
            // On fixture failure, break any pending read instead of leaking a pipe.
            if (!transferred) pipe.Dispose();
            else pipe.CloseChildWriter();
        }
    }

    internal static async Task RunAsync()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var pipe = await WindowsOverlappedOutputPipe.CreateAsync()
            .WaitAsync(TimeSpan.FromSeconds(12));

        // Writer remains connected and idle. The async read must remain pending.
        using (var stop = new CancellationTokenSource())
        {
            var pending = pipe.Parent.ReadAsync(new byte[128].AsMemory(), stop.Token).AsTask();
            await Task.Delay(150);
            if (pending.IsCompleted)
                throw new Exception("Idle connected writer did not hold the first read pending.");
            stop.Cancel();
            try
            {
                await pending.WaitAsync(TimeSpan.FromSeconds(5));
                throw new Exception("Cancellation of a real blocked overlapped read returned success.");
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }

        // Start a fresh read with an open writer and do NOT cancel it.
        // The owning pipe must not be disposed while this read is pending.
        var eofRead = pipe.Parent.ReadAsync(new byte[128].AsMemory()).AsTask();
        await Task.Delay(150);
        if (eofRead.IsCompleted)
            throw new Exception("Idle connected writer did not hold the EOF read pending.");
        pipe.CloseChildWriter(); // A real peer disconnect, not a fake completion callback.
        int count = await eofRead.WaitAsync(TimeSpan.FromSeconds(5));
        if (count != 0)
            throw new Exception($"Expected EOF after writer close, received {count} bytes.");
        Console.WriteLine("M3.3 real overlapped blocked-read cancellation and writer-close EOF passed");
    }
}
