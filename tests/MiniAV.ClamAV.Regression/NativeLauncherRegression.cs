using MiniAV.Engines.ClamAV;

// Opt-in M3.1 Windows-only launcher checks. Not a ClamAV integration test.
internal static class NativeLauncherRegression
{

    // Opt-in repeated-run check; handle count is a diagnostic, not a formal leak proof.
    internal static async Task RunRepeatedAsync()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        var args = new[] { "-NoProfile", "-NonInteractive", "-Command",
            "[Console]::Out.WriteLine('OK'); [Console]::Error.WriteLine('E')" };
        using var current = System.Diagnostics.Process.GetCurrentProcess();
        // Warm up CLR/PowerShell interop before measuring persistent handle growth.
        for (int i = 0; i < 3; i++)
            await WindowsSuspendedLauncher.RunAsync(exe, args, null,
                768L * 1024 * 1024, 4096, TimeSpan.FromSeconds(20), CancellationToken.None);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        current.Refresh();
        var baseline = current.HandleCount;
        const int iterations = 30;
        for (int i = 0; i < iterations; i++)
        {
            var result = await WindowsSuspendedLauncher.RunAsync(exe, args, null,
                768L * 1024 * 1024, 4096, TimeSpan.FromSeconds(20), CancellationToken.None);
            if (result.ExitCode != 0 || !result.StandardOutput.Contains("OK", StringComparison.Ordinal)
                || !result.StandardError.Contains("E", StringComparison.Ordinal))
                throw new Exception($"Native launcher repeated run failed at iteration {i}.");
            if ((i + 1) % 10 == 0)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                current.Refresh();
                Console.WriteLine($"M3.1 handles at run {i + 1}: {current.HandleCount}, delta={current.HandleCount - baseline}");
            }
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        current.Refresh();
        var after = current.HandleCount;
        Console.WriteLine($"M3.1 repeat diagnostics: iterations={iterations}, handles before={baseline}, after={after}, delta={after - baseline}");
        if (after - baseline > 30)
            throw new Exception($"Possible native handle leak: +{after - baseline} handles after {iterations} runs.");
        Console.WriteLine("M3.1 repeated native launcher regression passed");
    }


    // Verify a failure after Job assignment but before ResumeThread cannot execute
    // the child or leave a suspended orphan. Uses only a harmless file marker.
    internal static async Task RunPreResumeFailureAsync()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        var marker = Path.Combine(Path.GetTempPath(), "miniav-native-pre-resume-" +
            Guid.NewGuid().ToString("N") + ".txt");
        int pid = 0;
        try
        {
            var escaped = marker.Replace("'", "''", StringComparison.Ordinal);
            var argv = new[] { "-NoProfile", "-NonInteractive", "-Command",
                $"[IO.File]::WriteAllText('{escaped}', 'executed')" };
            try
            {
                await WindowsSuspendedLauncher.RunAsync(exe, argv, null,
                    768L * 1024 * 1024, 4096, TimeSpan.FromSeconds(20),
                    CancellationToken.None, childPid =>
                    {
                        pid = childPid;
                        throw new InjectedPreResumeException();
                    });
                throw new Exception("Injected pre-resume failure was ignored.");
            }
            catch (InjectedPreResumeException) { }

            if (pid <= 0) throw new Exception("Pre-resume injection was not reached.");
            if (File.Exists(marker))
                throw new Exception("Suspended child executed before resume.");
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(pid);
                if (!process.HasExited)
                    throw new Exception($"Suspended child still alive after cleanup: pid={pid}.");
            }
            catch (ArgumentException)
            {
                // OS already removed the terminated process from the process table.
            }
            Console.WriteLine("M3.1 pre-resume fault injection and suspended-child cleanup passed");
        }
        finally
        {
            if (File.Exists(marker)) File.Delete(marker);
        }
    }

    private sealed class InjectedPreResumeException : Exception { }


    // Stress cancellation while both pipe readers are consuming output.
    // A deadline limits the whole fixture so regressions cannot hang the runner.
    internal static async Task RunActiveIoCancellationAsync()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        var args = new[] { "-NoProfile", "-NonInteractive", "-Command",
            "while ($true) { [Console]::Out.WriteLine('STDOUT_ACTIVE'); [Console]::Error.WriteLine('STDERR_ACTIVE'); Start-Sleep -Milliseconds 10 }" };
        for (var i = 0; i < 5; ++i)
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(650));
            using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                await WindowsSuspendedLauncher.RunAsync(exe, args, null,
                    768L * 1024 * 1024, 32768, TimeSpan.FromSeconds(10),
                    cancellation.Token).WaitAsync(guard.Token);
                throw new Exception("Active-I/O child ignored cancellation.");
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested && !guard.IsCancellationRequested) { }
            catch (InvalidDataException)
            {
                // Unexpected flood is not acceptable: this fixture should cancel first.
                throw new Exception("Active-I/O test exceeded output cap before cancellation.");
            }
        }
        Console.WriteLine("M3.1 active stdout/stderr cancellation regression passed");
    }


    // A real descendant must die when the launcher Job is closed on cancellation.
    internal static async Task RunDescendantCancellationAsync()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        var marker = Path.Combine(Path.GetTempPath(),
            "miniav-descendant-" + Guid.NewGuid().ToString("N") + ".pid");
        int descendantPid = 0;
        try
        {
            var escaped = marker.Replace("'", "''", StringComparison.Ordinal);
            var script = "$child = Start-Process -FilePath '" +
                exe.Replace("'", "''", StringComparison.Ordinal) +
                "' -ArgumentList @('-NoProfile','-NonInteractive','-Command','Start-Sleep -Seconds 60') -PassThru; " +
                "[IO.File]::WriteAllText('" + escaped + "', [string]$child.Id); " +
                "while ($true) { Start-Sleep -Milliseconds 100 }";
            using var cancel = new CancellationTokenSource();
            using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var running = WindowsSuspendedLauncher.RunAsync(exe,
                new[] { "-NoProfile", "-NonInteractive", "-Command", script }, null,
                768L * 1024 * 1024, 32768, TimeSpan.FromSeconds(15), cancel.Token);
            try
            {
                // Require evidence of the child before requesting cancellation.
                while (!File.Exists(marker))
                {
                    if (running.IsCompleted)
                        throw new Exception("Native launcher exited before descendant creation.");
                    await Task.Delay(50, guard.Token);
                }
                descendantPid = int.Parse(await File.ReadAllTextAsync(marker, guard.Token),
                    System.Globalization.CultureInfo.InvariantCulture);
                if (descendantPid <= 0)
                    throw new Exception("Invalid descendant PID.");
                cancel.Cancel();
                try
                {
                    await running.WaitAsync(guard.Token);
                    throw new Exception("Descendant fixture ignored cancellation.");
                }
                catch (OperationCanceledException) when (cancel.IsCancellationRequested && !guard.IsCancellationRequested) { }
                // Give Windows a bounded window for exit observation after Job close.
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (DateTime.UtcNow < deadline && IsAlive(descendantPid))
                    await Task.Delay(50, guard.Token);
                if (IsAlive(descendantPid))
                    throw new Exception($"Job cleanup left descendant alive: pid={descendantPid}");
                Console.WriteLine("M3.1 cancellation kills descendant Job members passed");
            }
            finally
            {
                cancel.Cancel();
                try { await running.WaitAsync(TimeSpan.FromSeconds(10)); }
                catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
                catch (TimeoutException) { /* test will fail via guard or assertions */ }
            }
        }
        finally
        {
            // Defensive fixture cleanup; this does not count as launcher success.
            if (descendantPid > 0 && IsAlive(descendantPid))
            {
                using var survivor = System.Diagnostics.Process.GetProcessById(descendantPid);
                survivor.Kill(entireProcessTree: true);
                await survivor.WaitForExitAsync();
            }
            if (File.Exists(marker)) File.Delete(marker);
        }
    }

    private static bool IsAlive(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException) { return false; }
    }

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
