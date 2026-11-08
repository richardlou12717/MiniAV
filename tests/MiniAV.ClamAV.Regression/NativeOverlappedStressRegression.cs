using System.Diagnostics;
using MiniAV.Engines.ClamAV;

// Opt-in diagnostic for the real overlapped-output path. Handle deltas are
// signals for investigation, not a mathematical guarantee of leak freedom.
internal static class NativeOverlappedStressRegression
{
    internal static async Task RunAsync()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        static string[] Ps(string script) =>
            new[] { "-NoProfile", "-NonInteractive", "-Command", script };

        async Task OneAsync(int scenario)
        {
            using var cancel = new CancellationTokenSource();
            string script = scenario switch
            {
                0 => "[Console]::Out.WriteLine('OK'); [Console]::Error.WriteLine('OK'); exit 0",
                1 => "while ($true) { [Console]::Out.WriteLine('C'); Start-Sleep -Milliseconds 30 }",
                2 => "Start-Sleep -Seconds 30",
                _ => "for ($i=0; $i -lt 20000; $i++) { [Console]::Out.WriteLine('FLOOD'); [Console]::Error.WriteLine('FLOOD') }"
            };
            if (scenario == 1) cancel.CancelAfter(TimeSpan.FromMilliseconds(900));
            using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var run = WindowsSuspendedLauncher.RunAsync(exe, Ps(script), null,
                768L * 1024 * 1024, scenario == 3 ? 1024 : 32768,
                scenario == 2 ? TimeSpan.FromMilliseconds(900) : TimeSpan.FromSeconds(12),
                cancel.Token, useOverlappedOutputPipes: true);
            try
            {
                var result = await run.WaitAsync(guard.Token);
                if (scenario != 0 || result.ExitCode != 0 ||
                    !result.StandardOutput.Contains("OK", StringComparison.Ordinal) ||
                    !result.StandardError.Contains("OK", StringComparison.Ordinal))
                    throw new Exception($"Unexpected success/result for scenario {scenario}.");
            }
            catch (OperationCanceledException) when (scenario == 1 && cancel.IsCancellationRequested && !guard.IsCancellationRequested) { }
            catch (TimeoutException) when (scenario == 2 && !guard.IsCancellationRequested) { }
            catch (InvalidDataException) when (scenario == 3 && !guard.IsCancellationRequested) { }
        }

        const int iterations = 80;
        for (int i = 0; i < 4; i++) await OneAsync(i);
        using var current = Process.GetCurrentProcess();
        static void Collect()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        Collect();
        current.Refresh();
        int initial = current.HandleCount;
        int[] samples = new int[4];
        for (int i = 0; i < iterations; i++)
        {
            await OneAsync(i % 4);
            if ((i + 1) % 20 == 0)
            {
                Collect();
                current.Refresh();
                int delta = current.HandleCount - initial;
                samples[i / 20] = delta;
                Console.WriteLine($"M3.2 overlapped stress {i + 1}: handles={current.HandleCount}, delta={delta}");
            }
        }
        int finalDelta = samples[^1];
        Console.WriteLine($"M3.2 overlapped stress samples: {string.Join(", ", samples)}; final delta={finalDelta}");
        if (finalDelta > 40)
            throw new Exception($"Possible native overlapped handle leak: delta={finalDelta}");
        Console.WriteLine("M3.2 overlapped 80-iteration stress diagnostic passed");
    }
}
