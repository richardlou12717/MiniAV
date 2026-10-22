using MiniAV.Engines.ClamAV;

// Opt-in native runner compatibility test. Does not replace the production runner.
internal static class NativeRunnerCompatibilityRegression
{
    internal static async Task RunAsync()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        var runner = new NativeClamProcessRunnerPrototype();
        static string[] Ps(string script) =>
            new[] { "-NoProfile", "-NonInteractive", "-Command", script };

        foreach (var exit in new[] { 0, 1, 2 })
        {
            var result = await runner.RunAsync(exe, Ps(
                $"[Console]::Out.WriteLine('OUTPUT_{exit}'); " +
                $"[Console]::Error.WriteLine('ERROR_{exit}'); exit {exit}"),
                TimeSpan.FromSeconds(15), CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(20));
            if (result.ExitCode != exit ||
                !result.Stdout.Contains($"OUTPUT_{exit}", StringComparison.Ordinal) ||
                !result.Stderr.Contains($"ERROR_{exit}", StringComparison.Ordinal))
                throw new Exception($"Native compatibility exit/stdout/stderr mismatch at {exit}.");
        }
        Console.WriteLine("M3.3 native adapter exits 0/1/2 and stdout/stderr compatibility passed");

        try
        {
            await runner.RunAsync(exe, Ps(
                "for ($i = 0; $i -lt 10000; $i++) { [Console]::Out.WriteLine('0123456789'); [Console]::Error.WriteLine('0123456789') }"),
                TimeSpan.FromSeconds(15), CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(20));
            throw new Exception("Native compatibility output limit ignored.");
        }
        catch (ProcessOutputLimitException) { }
        Console.WriteLine("M3.3 native adapter output overflow translation passed");

        try
        {
            await runner.RunAsync(exe, Ps("Start-Sleep -Seconds 30"),
                TimeSpan.FromMilliseconds(900), CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(20));
            throw new Exception("Native compatibility deadline ignored.");
        }
        catch (TimeoutException ex) when (
            ex.Message.Contains("execution deadline", StringComparison.Ordinal)) { }

        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(900));
        try
        {
            await runner.RunAsync(exe, Ps("Start-Sleep -Seconds 30"),
                TimeSpan.FromSeconds(15), cancel.Token)
                .WaitAsync(TimeSpan.FromSeconds(20));
            throw new Exception("Native compatibility caller cancellation ignored.");
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        Console.WriteLine("M3.3 native adapter timeout and cancellation compatibility passed");
    }
}
