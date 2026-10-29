using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using MiniAV.Engines.ClamAV;

// Opt-in prototype check. Not yet an end-to-end suspended child I/O test.
internal static class NativeOverlappedPipeRegression
{
    internal static async Task RunAsync()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        Console.WriteLine("M3.1 pipe stage: connect");
        using var pipe = await WindowsOverlappedOutputPipe.CreateAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Console.WriteLine("M3.1 pipe stage: connected");
        if (!IsInheritable(pipe.Child))
            throw new Exception("Native overlapped child writer is not inheritable.");
        var bytes = new byte[] { 0, 1, 127, 128, 255 };
        var received = new byte[bytes.Length];
        Console.WriteLine("M3.1 pipe stage: start read bytes before write");
        var receiveTask = pipe.Parent.ReadExactlyAsync(received).AsTask();
        Console.WriteLine("M3.1 pipe stage: write");
        await pipe.ChildWriter.WriteAsync(bytes).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await pipe.ChildWriter.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Console.WriteLine("M3.1 pipe stage: await read bytes");
        await receiveTask.WaitAsync(TimeSpan.FromSeconds(5));
        if (!received.AsSpan().SequenceEqual(bytes))
            throw new Exception("Overlapped output pipe byte transfer mismatch.");

        // With writer still open and no incoming bytes, cancellation must terminate
        // a pending overlapped read; it must not merely time out the test watchdog.
        Console.WriteLine("M3.1 pipe stage: pending read and cancel");
        using var cancelled = new CancellationTokenSource();
        var pending = pipe.Parent.ReadAsync(new byte[16].AsMemory(), cancelled.Token).AsTask();
        await Task.Delay(100);
        if (pending.IsCompleted)
            throw new Exception("Read unexpectedly finished before cancellation.");
        cancelled.Cancel();
        try
        {
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            throw new Exception("Pending overlapped read ignored cancellation.");
        }
        catch (OperationCanceledException) when (cancelled.IsCancellationRequested) { }
        Console.WriteLine("M3.1 overlapped output pipe cancellation and transfer passed");
    }

    // End-to-end Windows suspended process smoke, still opt-in and non-production.
    internal static async Task RunLauncherAsync()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        Console.WriteLine("M3.1 overlapped launcher stage: start suspended child");
        var args = new[] { "-NoProfile", "-NonInteractive", "-Command",
            "[Console]::Out.WriteLine('OVERLAPPED_OUT'); [Console]::Error.WriteLine('OVERLAPPED_ERR'); exit 2" };
        var result = await WindowsSuspendedLauncher.RunAsync(exe, args, null,
            768L * 1024 * 1024, 4096, TimeSpan.FromSeconds(20),
            CancellationToken.None, useOverlappedOutputPipes: true)
            .WaitAsync(TimeSpan.FromSeconds(30));
        if (result.ExitCode != 2 ||
            !result.StandardOutput.Contains("OVERLAPPED_OUT", StringComparison.Ordinal) ||
            !result.StandardError.Contains("OVERLAPPED_ERR", StringComparison.Ordinal))
            throw new Exception("Suspended overlapped stdout/stderr/exit mismatch.");
        Console.WriteLine("M3.1 suspended overlapped stdout/stderr launcher smoke passed");
    }

    // Fault-path smoke. The test-only hook runs before stream owners are disposed.
    internal static async Task RunLauncherFailuresAsync()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        const long memory = 768L * 1024 * 1024;
        static string[] Ps(string script) =>
            new[] { "-NoProfile", "-NonInteractive", "-Command", script };
        int verified = 0;
        Action<bool, bool> RequireBoth(string scenario) => (stdout, stderr) =>
        {
            if (!stdout || !stderr)
                throw new Exception($"Reader not terminal at cleanup in {scenario}: stdout={stdout}, stderr={stderr}");
            Interlocked.Increment(ref verified);
            Console.WriteLine($"M3.1 overlapped {scenario}: both readers terminal before stream disposal");
        };

        Console.WriteLine("M3.1 overlapped failure stage: cancellation");
        using (var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(900)))
        {
            try
            {
                await WindowsSuspendedLauncher.RunAsync(exe,
                    Ps("while ($true) { [Console]::Out.WriteLine('ACTIVE'); [Console]::Error.WriteLine('ACTIVE'); Start-Sleep -Milliseconds 30 }"),
                    null, memory, 32768, TimeSpan.FromSeconds(15), cancel.Token,
                    useOverlappedOutputPipes: true,
                    readersQuiescedTestHook: RequireBoth("cancellation"))
                    .WaitAsync(TimeSpan.FromSeconds(20));
                throw new Exception("Cancellation was ignored.");
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        }

        Console.WriteLine("M3.1 overlapped failure stage: timeout");
        try
        {
            await WindowsSuspendedLauncher.RunAsync(exe,
                Ps("Start-Sleep -Seconds 30"),
                null, memory, 4096, TimeSpan.FromMilliseconds(900), CancellationToken.None,
                useOverlappedOutputPipes: true,
                readersQuiescedTestHook: RequireBoth("timeout"))
                .WaitAsync(TimeSpan.FromSeconds(20));
            throw new Exception("Deadline was ignored.");
        }
        catch (TimeoutException ex) when (ex.Message.Contains("execution deadline", StringComparison.Ordinal)) { }

        Console.WriteLine("M3.1 overlapped failure stage: output overflow");
        try
        {
            await WindowsSuspendedLauncher.RunAsync(exe,
                Ps("for ($i = 0; $i -lt 10000; $i++) { [Console]::Out.WriteLine('FLOOD'); [Console]::Error.WriteLine('FLOOD') }"),
                null, memory, 1024, TimeSpan.FromSeconds(15), CancellationToken.None,
                useOverlappedOutputPipes: true,
                readersQuiescedTestHook: RequireBoth("overflow"))
                .WaitAsync(TimeSpan.FromSeconds(20));
            throw new Exception("Output limit was ignored.");
        }
        catch (InvalidDataException) { }

        if (verified != 3)
            throw new Exception($"Reader completion hook ran {verified} times instead of three.");
        Console.WriteLine("M3.1 overlapped cancellation timeout overflow reader quiescence passed");
    }

    // An ordinary Job member holds stdout/stderr open after the parent exits.
    // Cancellation must close the Job and join both overlapped readers.
    internal static async Task RunRetainedWriterAsync()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        var marker = Path.Combine(Path.GetTempPath(),
            "miniav-overlapped-descendant-" + Guid.NewGuid().ToString("N") + ".pid");
        var escapedMarker = marker.Replace("'", "''", StringComparison.Ordinal);
        var escapedExe = exe.Replace("'", "''", StringComparison.Ordinal);
        var script = "$child = Start-Process -FilePath '" + escapedExe +
            "' -ArgumentList @('-NoProfile','-NonInteractive','-Command','Start-Sleep -Seconds 60') " +
            "-NoNewWindow -PassThru; " +
            "[IO.File]::WriteAllText('" + escapedMarker + "', [string]$child.Id); " +
            "[Console]::Out.WriteLine('PARENT_DONE'); [Console]::Error.WriteLine('PARENT_DONE')";
        int descendantPid = 0;
        int joined = 0;
        var parentExitedWithPendingReaders =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        try
        {
            Console.WriteLine("M3.2 retained-writer stage: start");
            var running = WindowsSuspendedLauncher.RunAsync(exe,
                new[] { "-NoProfile", "-NonInteractive", "-Command", script },
                null, 768L * 1024 * 1024, 32768, TimeSpan.FromSeconds(20),
                cancellation.Token, useOverlappedOutputPipes: true,
                readersQuiescedTestHook: (stdout, stderr) =>
                {
                    if (!stdout || !stderr)
                        throw new Exception("Retained-writer readers not terminal.");
                    Interlocked.Increment(ref joined);
                },
                parentExitedReadersPendingTestHook: (stdoutPending, stderrPending) =>
                {
                    if (stdoutPending && stderrPending)
                        parentExitedWithPendingReaders.TrySetResult();
                    else
                        parentExitedWithPendingReaders.TrySetException(
                            new Exception($"Parent exited without both readers pending: stdout={stdoutPending}, stderr={stderrPending}"));
                });
            try
            {
                var deadline = DateTime.UtcNow.AddSeconds(12);
                while (DateTime.UtcNow < deadline && descendantPid <= 0)
                {
                    if (File.Exists(marker))
                    {
                        var contents = await File.ReadAllTextAsync(marker);
                        int.TryParse(contents, out descendantPid);
                    }
                    if (descendantPid <= 0)
                    {
                        if (running.IsCompleted)
                            throw new Exception("Native launcher exited before descendant marker.");
                        await Task.Delay(50);
                    }
                }
                if (descendantPid <= 0) throw new TimeoutException("Descendant marker not observed.");
                Console.WriteLine("M3.2 retained-writer stage: verify parent exit and pending stdout/stderr");
                await parentExitedWithPendingReaders.Task.WaitAsync(TimeSpan.FromSeconds(8));
                if (running.IsCompleted)
                    throw new Exception("Launcher already completed although descendant should retain both writers.");
                Console.WriteLine("M3.2 retained-writer stage: cancel");
                cancellation.Cancel();
                try
                {
                    await running.WaitAsync(TimeSpan.FromSeconds(15));
                    throw new Exception("Retained-writer cancellation was ignored.");
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
                if (joined != 1)
                    throw new Exception($"Retained-writer reader completion hook count: {joined}");
                using var descendant = TryGetProcess(descendantPid);
                if (descendant is not null)
                {
                    try { await descendant.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
                    catch (TimeoutException)
                    {
                        throw new Exception("Retained-writer descendant still alive after Job close.");
                    }
                }
                Console.WriteLine("M3.2 parent-exited descendant-held pipes and reader cleanup passed");
            }
            finally
            {
                cancellation.Cancel();
                try { await running.WaitAsync(TimeSpan.FromSeconds(15)); }
                catch { /* Preserve primary fixture failure; defensive cleanup follows. */ }
            }
        }
        finally
        {
            if (descendantPid > 0)
            {
                using var survivor = TryGetProcess(descendantPid);
                if (survivor is not null && !survivor.HasExited)
                {
                    survivor.Kill(entireProcessTree: true);
                    await survivor.WaitForExitAsync();
                }
            }
            if (File.Exists(marker)) File.Delete(marker);
        }
    }

    // Deterministic double fault: pre-resume operation fails, cleanup confirms native
    // process exit, then the test-only hook injects an independent cleanup failure.
    internal static async Task RunCleanupErrorPreservationAsync()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        int pid = 0;
        try
        {
            await WindowsSuspendedLauncher.RunAsync(exe,
                new[] { "-NoProfile", "-NonInteractive", "-Command",
                    "[Console]::Out.WriteLine('MUST_NOT_RUN')" },
                null, 768L * 1024 * 1024, 4096, TimeSpan.FromSeconds(15),
                CancellationToken.None,
                beforeResumeTestHook: createdPid =>
                {
                    pid = createdPid;
                    throw new InvalidOperationException("INJECTED_OPERATION_FAILURE");
                },
                useOverlappedOutputPipes: true,
                cleanupFailureTestHook: () =>
                    throw new InvalidOperationException("INJECTED_CLEANUP_FAILURE"));
            throw new Exception("Double fault injection was ignored.");
        }
        catch (AggregateException ex)
        {
            if (ex.InnerExceptions.Count != 2 ||
                ex.InnerExceptions[0].Message != "INJECTED_OPERATION_FAILURE" ||
                ex.InnerExceptions[1] is not IOException cleanup ||
                cleanup.InnerException?.Message != "INJECTED_CLEANUP_FAILURE")
                throw new Exception("Both failures were not preserved in order.", ex);
        }
        if (pid <= 0) throw new Exception("Pre-resume injection was not reached.");
        using var process = TryGetProcess(pid);
        if (process is not null && !process.HasExited)
            throw new Exception("Injected suspended child survived cleanup.");
        Console.WriteLine("M3.2 native original and cleanup exception preservation passed");
    }

    // Real suspended/Job/overlapped launcher with a deterministic post-read hold.
    // The real read has completed; its task completion is held to force the actual
    // launcher cleanup join timeout without relying on unpredictable OS stalls.
    internal static async Task RunForcedDeferredJoinAsync()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ownerObserved = new TaskCompletionSource<DeferredReaderCleanup>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(900));
        try
        {
            Console.WriteLine("M3.2 forced launcher join stage: running");
            try
            {
                await WindowsSuspendedLauncher.RunAsync(exe,
                    new[] { "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 30" },
                    null, 768L * 1024 * 1024, 4096, TimeSpan.FromSeconds(15),
                    cancellation.Token, useOverlappedOutputPipes: true,
                    holdStdoutCompletionTestHook: () => release.Task,
                    readerJoinTimeoutTestOverride: TimeSpan.FromMilliseconds(150),
                    deferredOwnerTestHook: owner => ownerObserved.TrySetResult(owner))
                    .WaitAsync(TimeSpan.FromSeconds(15));
                throw new Exception("Forced reader join timeout unexpectedly succeeded.");
            }
            catch (IOException ex) when (ex.Message.Contains("deferred", StringComparison.Ordinal)) { }
            var owner = await ownerObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
            if (!owner.WasDeferred || owner.ResourcesDisposed)
                throw new Exception("Real launcher disposed owned streams before held reader completion.");
            Console.WriteLine("M3.2 forced launcher join stage: retained resources confirmed");
            release.SetResult();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!owner.ResourcesDisposed)
                await Task.Delay(10, deadline.Token);
            Console.WriteLine("M3.2 real launcher join timeout deferred cleanup passed");
        }
        finally
        {
            release.TrySetResult();
        }
    }

    // After the real Job is closed, inject a cleanup failure while stdout's
    // reader task remains held. Ownership must survive until the task finishes.
    internal static async Task RunJobCloseFailureOwnershipAsync()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        DeferredReaderCleanup? observed = null;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(900));
        try
        {
            Console.WriteLine("M3.2 Job-close fault stage: running");
            try
            {
                await WindowsSuspendedLauncher.RunAsync(exe,
                    new[] { "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 30" },
                    null, 768L * 1024 * 1024, 4096, TimeSpan.FromSeconds(15),
                    cancellation.Token, useOverlappedOutputPipes: true,
                    holdStdoutCompletionTestHook: () => release.Task,
                    readerJoinTimeoutTestOverride: TimeSpan.FromMilliseconds(150),
                    deferredOwnerTestHook: owner => observed = owner,
                    afterJobCloseTestHook: () =>
                        throw new InvalidOperationException("INJECTED_POST_JOB_CLOSE_FAILURE"))
                    .WaitAsync(TimeSpan.FromSeconds(15));
                throw new Exception("Job close fault returned success.");
            }
            catch (AggregateException ex)
            {
                if (ex.InnerExceptions.Count != 2 ||
                    ex.InnerExceptions[0].Message != "INJECTED_POST_JOB_CLOSE_FAILURE" ||
                    ex.InnerExceptions[1] is not IOException join ||
                    !join.Message.Contains("deferred", StringComparison.Ordinal))
                    throw new Exception("Job close and reader join failures not both preserved.", ex);
            }
            if (observed is null || !observed.WasDeferred || observed.ResourcesDisposed)
                throw new Exception("Reader resources released despite pending task and Job-close fault.");
            Console.WriteLine("M3.2 Job-close fault stage: ownership retained");
            release.SetResult();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!observed.ResourcesDisposed)
                await Task.Delay(10, deadline.Token);
            Console.WriteLine("M3.2 Job-close failure deferred reader ownership passed");
        }
        finally { release.TrySetResult(); }
    }

    internal static async Task RunTimeoutCleanupDoubleFaultAsync()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        try
        {
            await WindowsSuspendedLauncher.RunAsync(exe,
                new[] { "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 30" },
                null, 768L * 1024 * 1024, 4096,
                TimeSpan.FromMilliseconds(900), CancellationToken.None,
                useOverlappedOutputPipes: true,
                cleanupFailureTestHook: () =>
                    throw new InvalidOperationException("INJECTED_TIMEOUT_CLEANUP_FAILURE"))
                .WaitAsync(TimeSpan.FromSeconds(20));
            throw new Exception("Timed out subprocess unexpectedly succeeded.");
        }
        catch (AggregateException ex)
        {
            if (ex.InnerExceptions.Count != 2 ||
                ex.InnerExceptions[0] is not TimeoutException timeout ||
                !timeout.Message.Contains("execution deadline", StringComparison.Ordinal) ||
                ex.InnerExceptions[1] is not IOException cleanup ||
                cleanup.InnerException?.Message != "INJECTED_TIMEOUT_CLEANUP_FAILURE")
                throw new Exception("Deadline timeout and cleanup error were not both preserved.", ex);
        }
        Console.WriteLine("M3.3 native timeout plus cleanup double fault passed");
    }


    // A real suspended parent exits, but its Job descendant retains both output
    // writers. Deadline (not caller cancellation) must kill the Job and join reads.
    internal static async Task RunRetainedWriterTimeoutAsync()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        var marker = Path.Combine(Path.GetTempPath(),
            "miniav-retained-timeout-" + Guid.NewGuid().ToString("N") + ".pid");
        var escapedMarker = marker.Replace("'", "''", StringComparison.Ordinal);
        var escapedExe = exe.Replace("'", "''", StringComparison.Ordinal);
        var script = "$child = Start-Process -FilePath '" + escapedExe +
            "' -ArgumentList @('-NoProfile','-NonInteractive','-Command','Start-Sleep -Seconds 60') " +
            "-NoNewWindow -PassThru; " +
            "[IO.File]::WriteAllText('" + escapedMarker + "', [string]$child.Id); " +
            "[Console]::Out.WriteLine('PARENT_DONE'); [Console]::Error.WriteLine('PARENT_DONE')";
        int pid = 0, quiesced = 0;
        var parentPending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var run = WindowsSuspendedLauncher.RunAsync(exe,
                new[] { "-NoProfile", "-NonInteractive", "-Command", script },
                null, 768L * 1024 * 1024, 32768, TimeSpan.FromSeconds(8),
                CancellationToken.None, useOverlappedOutputPipes: true,
                parentExitedReadersPendingTestHook: (stdout, stderr) =>
                {
                    if (stdout && stderr) parentPending.TrySetResult();
                    else parentPending.TrySetException(
                        new Exception($"Parent exit reader state stdout={stdout} stderr={stderr}"));
                },
                readersQuiescedTestHook: (stdout, stderr) =>
                {
                    if (!stdout || !stderr)
                        throw new Exception("Retained-writer timeout readers did not quiesce.");
                    Interlocked.Increment(ref quiesced);
                });
            try
            {
                await parentPending.Task.WaitAsync(TimeSpan.FromSeconds(6));
                if (!File.Exists(marker))
                    throw new Exception("Descendant marker missing at parent exit.");
                if (!int.TryParse(await File.ReadAllTextAsync(marker), out pid) || pid <= 0)
                    throw new Exception("Invalid descendant PID marker.");
                if (run.IsCompleted)
                    throw new Exception("Launcher completed before retained-writer deadline.");
                try
                {
                    await run.WaitAsync(TimeSpan.FromSeconds(15));
                    throw new Exception("Retained-writer deadline was ignored.");
                }
                catch (TimeoutException ex) when (ex.Message.Contains("execution deadline", StringComparison.Ordinal)) { }
                if (quiesced != 1) throw new Exception("Reader quiescence hook not observed.");
                using var descendant = TryGetProcess(pid);
                if (descendant is not null && !descendant.HasExited)
                    await descendant.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                Console.WriteLine("M3.3 retained-writer descendant timeout and paired reader cleanup passed");
            }
            finally
            {
                if (!run.IsCompleted)
                    try { await run.WaitAsync(TimeSpan.FromSeconds(15)); }
                    catch { /* preserve primary assertion; fixture cleanup below */ }
            }
        }
        finally
        {
            if (pid > 0)
            {
                using var survivor = TryGetProcess(pid);
                if (survivor is not null && !survivor.HasExited)
                {
                    survivor.Kill(entireProcessTree: true);
                    await survivor.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                }
            }
            if (File.Exists(marker)) File.Delete(marker);
        }
    }


    // Output overflow while an actual Job descendant holds inherited writers.
    // The parent deliberately floods after creating and recording its child.
    internal static async Task RunRetainedWriterOverflowAsync()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        var marker = Path.Combine(Path.GetTempPath(),
            "miniav-retained-overflow-" + Guid.NewGuid().ToString("N") + ".pid");
        var escapedMarker = marker.Replace("'", "''", StringComparison.Ordinal);
        var escapedExe = exe.Replace("'", "''", StringComparison.Ordinal);
        var script = "$child = Start-Process -FilePath '" + escapedExe +
            "' -ArgumentList @('-NoProfile','-NonInteractive','-Command','Start-Sleep -Seconds 60') " +
            "-NoNewWindow -PassThru; " +
            "[IO.File]::WriteAllText('" + escapedMarker + "', [string]$child.Id); " +
            "for ($i = 0; $i -lt 5000; $i++) { " +
            "[Console]::Out.WriteLine('FLOOD'); [Console]::Error.WriteLine('FLOOD') }";
        int pid = 0, quiesced = 0;
        try
        {
            try
            {
                await WindowsSuspendedLauncher.RunAsync(exe,
                    new[] { "-NoProfile", "-NonInteractive", "-Command", script },
                    null, 768L * 1024 * 1024, 1024, TimeSpan.FromSeconds(20),
                    CancellationToken.None, useOverlappedOutputPipes: true,
                    readersQuiescedTestHook: (stdout, stderr) =>
                    {
                        if (!stdout || !stderr)
                            throw new Exception("Overflow left a native reader pending.");
                        Interlocked.Increment(ref quiesced);
                    }).WaitAsync(TimeSpan.FromSeconds(25));
                throw new Exception("Retained-descendant output overflow was ignored.");
            }
            catch (InvalidDataException ex) when (
                ex.Message.Contains("output exceeded", StringComparison.Ordinal)) { }

            if (!File.Exists(marker) ||
                !int.TryParse(await File.ReadAllTextAsync(marker), out pid) || pid <= 0)
                throw new Exception("Overflow fixture descendant PID was not recorded.");
            if (quiesced != 1)
                throw new Exception("Both-reader cleanup confirmation did not run.");
            using var descendant = TryGetProcess(pid);
            if (descendant is not null && !descendant.HasExited)
                await descendant.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Console.WriteLine("M3.3 retained-writer descendant overflow and paired reader cleanup passed");
        }
        finally
        {
            // Defensive fixture cleanup is never counted as launcher success.
            if (pid == 0 && File.Exists(marker))
                int.TryParse(await File.ReadAllTextAsync(marker), out pid);
            if (pid > 0)
            {
                using var survivor = TryGetProcess(pid);
                if (survivor is not null && !survivor.HasExited)
                {
                    survivor.Kill(entireProcessTree: true);
                    await survivor.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                }
            }
            if (File.Exists(marker)) File.Delete(marker);
        }
    }

    private static System.Diagnostics.Process? TryGetProcess(int pid)
    {
        try { return System.Diagnostics.Process.GetProcessById(pid); }
        catch (ArgumentException) { return null; }
    }

    private static bool IsInheritable(SafePipeHandle handle)
    {
        if (!GetHandleInformation(handle, out var flags))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return (flags & 1u) != 0;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetHandleInformation(SafePipeHandle handle, out uint flags);
}
