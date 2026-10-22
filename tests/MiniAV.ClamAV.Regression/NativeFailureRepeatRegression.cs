using System.Diagnostics;
using MiniAV.Engines.ClamAV;

// Opt-in, Windows-only diagnostic. Exception checks are exact and timeout-guarded.
internal static class NativeFailureRepeatRegression
{
    internal static async Task RunAsync()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        const long memory = 768L * 1024 * 1024;
        static string[] Args(string script) => new[] { "-NoProfile", "-NonInteractive", "-Command", script };

        async Task RunOneAsync(int scenario)
        {
            using var cancel = new CancellationTokenSource();
            var script = scenario switch
            {
                0 => "while ($true) { [Console]::Out.WriteLine('C'); [Console]::Error.WriteLine('C'); Start-Sleep -Milliseconds 20 }",
                1 => "while ($true) { Start-Sleep -Milliseconds 100 }",
                _ => "while ($true) { [Console]::Out.WriteLine('FLOOD'); [Console]::Error.WriteLine('FLOOD') }"
            };
            if (scenario == 0) cancel.CancelAfter(TimeSpan.FromMilliseconds(700));
            using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            var run = WindowsSuspendedLauncher.RunAsync(exe, Args(script), null, memory,
                scenario == 2 ? 1024 : 32768,
                scenario == 1 ? TimeSpan.FromMilliseconds(700) : TimeSpan.FromSeconds(10), cancel.Token);
            try
            {
                await run.WaitAsync(guard.Token);
                throw new Exception($"Native failure scenario {scenario} unexpectedly succeeded.");
            }
            catch (OperationCanceledException) when (scenario == 0 && cancel.IsCancellationRequested && !guard.IsCancellationRequested) { }
            catch (TimeoutException) when (scenario == 1 && !guard.IsCancellationRequested) { }
            catch (InvalidDataException) when (scenario == 2 && !guard.IsCancellationRequested) { }
        }

        // Warm up JIT/interop and managed runtime before measuring.
        for (var scenario = 0; scenario < 3; scenario++) await RunOneAsync(scenario);
        using var current = Process.GetCurrentProcess();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        current.Refresh();
        var baseline = current.HandleCount;
        for (var i = 0; i < 30; i++)
        {
            await RunOneAsync(i % 3);
            if ((i + 1) % 10 == 0)
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                current.Refresh();
                Console.WriteLine($"M3.1 failure repeats {i + 1}: handles={current.HandleCount}, delta={current.HandleCount - baseline}");
            }
        }
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        current.Refresh();
        var delta = current.HandleCount - baseline;
        Console.WriteLine($"M3.1 failure-repeat diagnostics: initial={baseline}, final={current.HandleCount}, delta={delta}");
        if (delta > 30) throw new Exception($"Possible exceptional-path handle leak: delta={delta}");
        Console.WriteLine("M3.1 repeated cancellation timeout overflow regression passed");
    }
}
