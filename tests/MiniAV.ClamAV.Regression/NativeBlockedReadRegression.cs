using MiniAV.Engines.ClamAV;

// M3.3: real pending kernel pipe reads, not a synthetic task-completion gate.
// All children are in-process safe pipe handles; no production runner changes.
internal static class NativeBlockedReadRegression
{
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
