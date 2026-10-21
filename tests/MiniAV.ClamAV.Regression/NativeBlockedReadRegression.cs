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

    // A genuine overlapped read remains pending during the owner's bounded join.
    // Cancellation, not writer EOF, must complete the read and release ownership.
    internal static async Task RunDeferredCancellationAsync()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var pipe = await WindowsOverlappedOutputPipe.CreateAsync()
            .WaitAsync(TimeSpan.FromSeconds(12));
        using var stop = new CancellationTokenSource();
        Task<int>? pending = null;
        DeferredReaderCleanup? owner = null;
        try
        {
            pending = pipe.Parent.ReadAsync(new byte[128].AsMemory(), stop.Token).AsTask();
            await Task.Delay(150);
            if (pending.IsCompleted)
                throw new Exception("Real read was not pending before deferred cancellation.");

            owner = new DeferredReaderCleanup(new IDisposable[] { pipe }, pending);
            try
            {
                await owner.JoinOrDeferAsync(TimeSpan.FromMilliseconds(100));
                throw new Exception("Pending overlapped read unexpectedly joined.");
            }
            catch (IOException ex) when (ex.Message.Contains("deferred", StringComparison.Ordinal)) { }

            if (!owner.WasDeferred || owner.ResourcesDisposed || owner.CleanupCompletion.IsCompleted)
                throw new Exception("Deferred owner released a pipe with a pending read.");

            stop.Cancel(); // Writer stays open: only I/O cancellation can settle the read.
            try
            {
                await pending.WaitAsync(TimeSpan.FromSeconds(5));
                throw new Exception("Cancelled blocked read unexpectedly succeeded.");
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }

            var cleanupFailure = await owner.CleanupCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            if (cleanupFailure is not null || !owner.ResourcesDisposed)
                throw new Exception("Deferred cancellation did not release owned pipe cleanly.");
            Console.WriteLine("M3.3 real blocked-read deferred cancellation and disposal passed");
        }
        finally
        {
            stop.Cancel();
            // If the reader was never transferred, dispose directly.
            // If transferred, the owner alone must dispose after task completion.
            if (owner is null)
                pipe.Dispose();
        }
    }

    // Two real pending overlapped reads share one deferred owner. Closing only
    // stdout's writer must not release stderr's live read or its pipe.
    internal static async Task RunPairedDeferredAsync()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        WindowsOverlappedOutputPipe? stdout = null, stderr = null;
        DeferredReaderCleanup? owner = null;
        try
        {
            stdout = await WindowsOverlappedOutputPipe.CreateAsync().WaitAsync(TimeSpan.FromSeconds(12));
            stderr = await WindowsOverlappedOutputPipe.CreateAsync().WaitAsync(TimeSpan.FromSeconds(12));
            var outRead = stdout.Parent.ReadAsync(new byte[128].AsMemory()).AsTask();
            var errRead = stderr.Parent.ReadAsync(new byte[128].AsMemory()).AsTask();
            await Task.Delay(150);
            if (outRead.IsCompleted || errRead.IsCompleted)
                throw new Exception("Both idle-writer reads must be pending before cleanup.");

            owner = new DeferredReaderCleanup(new IDisposable[] { stdout, stderr }, outRead, errRead);
            try
            {
                await owner.JoinOrDeferAsync(TimeSpan.FromMilliseconds(100));
                throw new Exception("Paired pending reads unexpectedly joined.");
            }
            catch (IOException ex) when (ex.Message.Contains("deferred", StringComparison.Ordinal)) { }

            if (!owner.WasDeferred || owner.ResourcesDisposed || owner.CleanupCompletion.IsCompleted)
                throw new Exception("Paired owner prematurely released blocked reads.");
            stdout.CloseChildWriter();
            if (await outRead.WaitAsync(TimeSpan.FromSeconds(5)) != 0)
                throw new Exception("Stdout did not reach EOF.");
            if (errRead.IsCompleted || owner.ResourcesDisposed || owner.CleanupCompletion.IsCompleted)
                throw new Exception("Stdout EOF incorrectly released pending stderr ownership.");
            stderr.CloseChildWriter();
            if (await errRead.WaitAsync(TimeSpan.FromSeconds(5)) != 0)
                throw new Exception("Stderr did not reach EOF.");
            var failure = await owner.CleanupCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            if (failure is not null || !owner.ResourcesDisposed)
                throw new Exception("Paired deferred owner failed to dispose after both reads.");
            Console.WriteLine("M3.3 paired real blocked stdout/stderr deferred ownership passed");
        }
        finally
        {
            // Break both reads on a failed assertion. The owner must exclusively
            // dispose transferred resources once reader tasks reach completion.
            stdout?.CloseChildWriter();
            stderr?.CloseChildWriter();
            if (owner is null)
            {
                stdout?.Dispose();
                stderr?.Dispose();
            }
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
