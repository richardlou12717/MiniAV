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
        static Action<bool, bool> RequireBoth(string scenario) => (stdout, stderr) =>
        {
            if (!stdout || !stderr)
                throw new Exception($"Reader not terminal at cleanup in {scenario}: stdout={stdout}, stderr={stderr}");
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

        Console.WriteLine("M3.1 overlapped cancellation timeout overflow reader quiescence passed");
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
