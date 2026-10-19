using MiniAV.Engines.ClamAV;

// Opt-in M3.1 Windows-only launcher checks. Not a ClamAV integration test.
internal static class NativeLauncherRegression
{
    internal static async Task RunAsync()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        const long memory = 768L * 1024 * 1024;
        static IReadOnlyList<string> Ps(string script) =>
            new[] { "-NoProfile", "-NonInteractive", "-Command", script };
        var ok = await WindowsSuspendedLauncher.RunAsync(exe,
            Ps("[Console]::Out.WriteLine('OUT_OK'); [Console]::Error.WriteLine('ERR_OK'); exit 2"),
            null, memory, 4096, TimeSpan.FromSeconds(20), CancellationToken.None);
        if (ok.ExitCode != 2 || !ok.StandardOutput.Contains("OUT_OK", StringComparison.Ordinal)
            || !ok.StandardError.Contains("ERR_OK", StringComparison.Ordinal))
            throw new Exception("Native launcher stdout/stderr/exit regression failed.");

        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            try
            {
                await WindowsSuspendedLauncher.RunAsync(exe, Ps("exit 0"), null, memory,
                    4096, TimeSpan.FromSeconds(20), cancelled.Token);
                throw new Exception("Pre-launch cancellation was ignored.");
            }
            catch (OperationCanceledException) when (cancelled.IsCancellationRequested) { }
        }
        try
        {
            await WindowsSuspendedLauncher.RunAsync(exe, Ps("Start-Sleep -Seconds 30"), null,
                memory, 4096, TimeSpan.FromMilliseconds(800), CancellationToken.None);
            throw new Exception("Timeout was ignored.");
        }
        catch (TimeoutException) { }

        using (var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(800)))
        {
            try
            {
                await WindowsSuspendedLauncher.RunAsync(exe, Ps("Start-Sleep -Seconds 30"),
                    null, memory, 4096, TimeSpan.FromSeconds(20), cancellation.Token);
                throw new Exception("Running child cancellation was ignored.");
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        }
        try
        {
            await WindowsSuspendedLauncher.RunAsync(exe,
                Ps("for ($i = 0; $i -lt 50000; $i++) { [Console]::Out.WriteLine('FLOOD'); [Console]::Error.WriteLine('FLOOD') }"),
                null, memory, 1024, TimeSpan.FromSeconds(20), CancellationToken.None);
            throw new Exception("Output cap was ignored.");
        }
        catch (InvalidDataException) { }
        Console.WriteLine("M3.1 isolated native launcher smoke, cancellation, timeout and output cap passed");
    }
}
