using System.Diagnostics;
using System.Globalization;
using MiniAV.Engines.ClamAV;

// Explicit opt-in Windows fixture: one real Job descendant, then timeout or output overflow.
internal static class NativeDescendantFailureRegression
{
    internal static async Task RunAsync()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        await CheckAsync(overflow: false);
        await CheckAsync(overflow: true);
        Console.WriteLine("M3.1 timeout and output-overflow descendant cleanup passed");
    }

    private static async Task CheckAsync(bool overflow)
    {
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        var marker = Path.Combine(Path.GetTempPath(), "miniav-desc-failure-" +
            Guid.NewGuid().ToString("N") + ".pid");
        var pid = 0;
        Task<WindowsSuspendedLauncher.Result>? running = null;
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        try
        {
            var escapedExe = exe.Replace("'", "''", StringComparison.Ordinal);
            var escapedMarker = marker.Replace("'", "''", StringComparison.Ordinal);
            // Spawn a harmless sleeping grandchild. Wait for the marker before triggering
            // overflow, so termination cannot be counted as PASS without a descendant.
            var script = "$p = Start-Process -FilePath '" + escapedExe +
                "' -ArgumentList @('-NoProfile','-NonInteractive','-Command','Start-Sleep -Seconds 60') -PassThru; " +
                "[IO.File]::WriteAllText('" + escapedMarker + "', [string]$p.Id); " +
                (overflow
                    ? "Start-Sleep -Milliseconds 800; while ($true) { [Console]::Out.WriteLine('FLOOD_OUTPUT'); [Console]::Error.WriteLine('FLOOD_ERROR') }"
                    : "while ($true) { Start-Sleep -Milliseconds 50 }");
            running = WindowsSuspendedLauncher.RunAsync(exe,
                new[] { "-NoProfile", "-NonInteractive", "-Command", script },
                null, 768L * 1024 * 1024, overflow ? 1024 : 32768,
                overflow ? TimeSpan.FromSeconds(20) : TimeSpan.FromSeconds(3),
                CancellationToken.None);

            while (pid == 0)
            {
                if (running.IsCompleted)
                {
                    // Observe actual failure, but it is not a PASS before the marker exists.
                    try { await running; }
                    catch (Exception e) { throw new Exception("Launcher finished before descendant marker.", e); }
                    throw new Exception("Launcher exited before descendant marker.");
                }
                if (File.Exists(marker))
                {
                    try
                    {
                        var value = await File.ReadAllTextAsync(marker, guard.Token);
                        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out pid);
                    }
                    catch (IOException) { /* marker writer may still have file open */ }
                }
                if (pid == 0) await Task.Delay(25, guard.Token);
            }
            // Ensure the recorded descendant exists before accepting its later termination.
            if (!IsAlive(pid))
                throw new Exception("Descendant exited before the failure condition.");
            try
            {
                await running.WaitAsync(guard.Token);
                throw new Exception("Launcher succeeded instead of failing closed.");
            }
            catch (TimeoutException) when (!overflow && !guard.IsCancellationRequested) { }
            catch (InvalidDataException) when (overflow && !guard.IsCancellationRequested) { }

            var until = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (IsAlive(pid) && DateTime.UtcNow < until)
                await Task.Delay(50, guard.Token);
            if (IsAlive(pid))
                throw new Exception("Descendant survived native " +
                    (overflow ? "output overflow" : "timeout") + ": PID=" + pid);
            Console.WriteLine(overflow
                ? "M3.1 output-overflow descendant cleanup passed"
                : "M3.1 timeout descendant cleanup passed");
        }
        finally
        {
            // Test cleanup is only a safety fallback. It never changes a failed
            // assertion into success or counts as launcher cleanup.
            if (pid > 0 && IsAlive(pid))
            {
                using var survivor = Process.GetProcessById(pid);
                survivor.Kill(entireProcessTree: true);
                await survivor.WaitForExitAsync();
            }
            if (File.Exists(marker)) File.Delete(marker);
            if (running is { IsCompleted: false })
            {
                // An unfinished launcher is a failure; avoid unobserved exceptions.
                _ = running.ContinueWith(t => { _ = t.Exception; },
                    TaskContinuationOptions.OnlyOnFaulted);
            }
        }
    }

    private static bool IsAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException) { return false; }
    }
}
