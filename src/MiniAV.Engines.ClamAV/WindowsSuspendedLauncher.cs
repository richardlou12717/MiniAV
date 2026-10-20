using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MiniAV.Engines.ClamAV;

/// <summary>
/// Experimental M3.1 native launcher. Not wired into production scanning.
/// Job assignment precedes execution; failure never falls back to Process.Start.
/// </summary>
internal static class WindowsSuspendedLauncher
{
    private const uint Suspended = 0x4, NoWindow = 0x08000000, ExtendedInfo = 0x00080000;
    private const uint UseStdHandles = 0x100;
    private const int HandleListAttribute = 0x00020002;
    private const uint WaitObject0 = 0, WaitTimeout = 258, WaitFailed = 0xFFFFFFFF;
    internal sealed record Result(int ExitCode, string StandardOutput, string StandardError);

    internal static async Task<Result> RunAsync(string executable, IReadOnlyList<string> args,
        string? workingDirectory, long jobMemoryLimitBytes, int maxCharsPerStream,
        TimeSpan timeout, CancellationToken cancellationToken,
        Action<int>? beforeResumeTestHook = null,
        bool useOverlappedOutputPipes = false)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable))
            throw new ArgumentException("Executable must be an existing absolute file.", nameof(executable));
        if (maxCharsPerStream < 1 || timeout <= TimeSpan.Zero || jobMemoryLimitBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxCharsPerStream), "Invalid process limits.");
        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var token = deadline.Token;
        using var stdin = WindowsChildPipe.Create(childReads: true);
        using var stdout = useOverlappedOutputPipes ? null : WindowsChildPipe.Create(childReads: false);
        using var stderr = useOverlappedOutputPipes ? null : WindowsChildPipe.Create(childReads: false);
        using var asyncStdout = useOverlappedOutputPipes ? await WindowsOverlappedOutputPipe.CreateAsync().ConfigureAwait(false) : null;
        using var asyncStderr = useOverlappedOutputPipes ? await WindowsOverlappedOutputPipe.CreateAsync().ConfigureAwait(false) : null;
        var handles = new[] { stdin.Child.DangerousGetHandle(),
            useOverlappedOutputPipes ? asyncStdout!.Child.DangerousGetHandle() : stdout!.Child.DangerousGetHandle(),
            useOverlappedOutputPipes ? asyncStderr!.Child.DangerousGetHandle() : stderr!.Child.DangerousGetHandle() };
        IntPtr attributes = IntPtr.Zero, listMemory = IntPtr.Zero;
        bool initialized = false;
        ProcessInformation native = default;
        ClamJob? job = null;
        Process? child = null;
        try
        {
            IntPtr attributeBytes = IntPtr.Zero;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attributeBytes);
            if (attributeBytes == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            attributes = Marshal.AllocHGlobal(attributeBytes);
            if (!InitializeProcThreadAttributeList(attributes, 1, 0, ref attributeBytes))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Attribute list initialization failed.");
            initialized = true;
            listMemory = Marshal.AllocHGlobal(handles.Length * IntPtr.Size);
            for (int i = 0; i < handles.Length; ++i)
                Marshal.WriteIntPtr(listMemory, i * IntPtr.Size, handles[i]);
            if (!UpdateProcThreadAttribute(attributes, 0, (IntPtr)HandleListAttribute, listMemory,
                    (IntPtr)(handles.Length * IntPtr.Size), IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Handle allowlist failed.");
            var startup = new StartupInfoEx
            {
                StartupInfo = new StartupInfo
                {
                    Cb = (uint)Marshal.SizeOf<StartupInfoEx>(),
                    Flags = UseStdHandles,
                    StdInput = handles[0], StdOutput = handles[1], StdError = handles[2]
                },
                AttributeList = attributes
            };
            token.ThrowIfCancellationRequested();
            var command = new StringBuilder(WindowsArgumentEncoding.Build(executable, args));
            if (!CreateProcessW(executable, command, IntPtr.Zero, IntPtr.Zero, true,
                    Suspended | NoWindow | ExtendedInfo, IntPtr.Zero, workingDirectory,
                    ref startup, out native))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Suspended process launch failed.");
            // The suspended process must be terminated if anything below fails.
            child = Process.GetProcessById(checked((int)native.ProcessId));
            job = ClamJob.Attach(child, jobMemoryLimitBytes);
            // Test-only injection runs after Job attachment but before any child code.
            // A throwing callback exercises cleanup of a still-suspended process.
            beforeResumeTestHook?.Invoke(checked((int)native.ProcessId));
            token.ThrowIfCancellationRequested();
            if (ResumeThread(native.ThreadHandle) != 1)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "ResumeThread failed.");
            stdin.Child.Dispose();
            if (useOverlappedOutputPipes)
            {
                asyncStdout!.CloseChildWriter();
                asyncStderr!.CloseChildWriter();
            }
            else
            {
                stdout!.Child.Dispose();
                stderr!.Child.Dispose();
            }
            stdin.Parent.Dispose();
            using var stdoutStream = useOverlappedOutputPipes ? null :
                new FileStream(stdout!.Parent, FileAccess.Read, 4096, isAsync: false);
            using var stderrStream = useOverlappedOutputPipes ? null :
                new FileStream(stderr!.Parent, FileAccess.Read, 4096, isAsync: false);
            using var outReader = new StreamReader(useOverlappedOutputPipes ? asyncStdout!.Parent : stdoutStream!, Encoding.UTF8, true, bufferSize: 4096, leaveOpen: true);
            using var errReader = new StreamReader(useOverlappedOutputPipes ? asyncStderr!.Parent : stderrStream!, Encoding.UTF8, true, leaveOpen: true);
            using var readStop = CancellationTokenSource.CreateLinkedTokenSource(token);
            Task<string>? outTask = null, errTask = null;
            Task? waitTask = null;
            try
            {
                outTask = ReadBoundedAsync(outReader, maxCharsPerStream, readStop.Token);
                errTask = ReadBoundedAsync(errReader, maxCharsPerStream, readStop.Token);
                waitTask = child.WaitForExitAsync(token);
                var pending = new List<Task> { outTask, errTask, waitTask };
                var cancellationSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using var cancellationRegistration = token.Register(
                    static state => ((TaskCompletionSource)state!).TrySetResult(), cancellationSignal);
                while (pending.Count != 0)
                {
                    var completed = await Task.WhenAny(pending.Append(cancellationSignal.Task)).ConfigureAwait(false);
                    if (completed == cancellationSignal.Task)
                    {
                        token.ThrowIfCancellationRequested();
                        throw new OperationCanceledException(token);
                    }
                    await completed.ConfigureAwait(false);
                    pending.Remove(completed);
                }
                token.ThrowIfCancellationRequested();
                return new Result(child.ExitCode, outTask.Result, errTask.Result);
            }
            finally
            {
                // On overflow/cancel/timeout, stop readers and kill the entire Job
                // *before* waiting for EOF. Close owning streams only after joins.
                readStop.Cancel();
                job?.Dispose();
                job = null;
                var readers = new List<Task>(2);
                if (outTask is not null) readers.Add(outTask);
                if (errTask is not null) readers.Add(errTask);
                if (readers.Count != 0)
                {
                    try
                    {
                        await Task.WhenAll(readers).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (readStop.IsCancellationRequested) { }
                    catch (InvalidDataException) { /* overflow already being propagated */ }
                    catch (TimeoutException ex)
                    {
                        throw new IOException("Native pipe readers did not stop after Job termination.", ex);
                    }
                }
                // A process wait task also receives the cancelled token; observe its
                // completion without requiring success to be treated as scan success.
                if (waitTask is { IsCompleted: true })
                    _ = waitTask.Exception;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw new TimeoutException("Native child exceeded its execution deadline.");
        }
        finally
        {
            // Always release every handle even when process termination fails.
            // A failed wait is a fatal cleanup error, never a successful scan.
            Exception? cleanupFailure = null;
            try
            {
                job?.Dispose(); // Kill all Job members before checking process exit.
                if (native.ProcessHandle != IntPtr.Zero)
                {
                    // Job close triggers process-tree termination asynchronously.
                    // Let it complete before attempting TerminateProcess: a process
                    // already terminating can reject that call with ACCESS_DENIED.
                    var wait = WaitForSingleObject(native.ProcessHandle, 5000);
                    if (wait == WaitTimeout)
                    {
                        if (!TerminateProcess(native.ProcessHandle, 91))
                        {
                            var error = Marshal.GetLastWin32Error();
                            // A concurrent exit is harmless only when confirmed signaled.
                            wait = WaitForSingleObject(native.ProcessHandle, 1000);
                            if (wait != WaitObject0)
                                throw new Win32Exception(error, "Native child termination failed and exit was not confirmed.");
                        }
                        else
                        {
                            wait = WaitForSingleObject(native.ProcessHandle, 5000);
                        }
                    }
                    if (wait != WaitObject0)
                        throw wait == WaitFailed
                            ? new Win32Exception(Marshal.GetLastWin32Error(), "Native child wait failed.")
                            : new IOException($"Native child did not exit during cleanup (wait={wait}).");
                }
            }
            catch (Exception ex)
            {
                cleanupFailure = ex;
            }
            finally
            {
                if (native.ThreadHandle != IntPtr.Zero) CloseHandle(native.ThreadHandle);
                if (native.ProcessHandle != IntPtr.Zero) CloseHandle(native.ProcessHandle);
                child?.Dispose();
                if (initialized) DeleteProcThreadAttributeList(attributes);
                if (listMemory != IntPtr.Zero) Marshal.FreeHGlobal(listMemory);
                if (attributes != IntPtr.Zero) Marshal.FreeHGlobal(attributes);
            }
            if (cleanupFailure is not null)
                throw new IOException("Native process cleanup could not be confirmed.", cleanupFailure);
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, int maxChars, CancellationToken token)
    {
        var builder = new StringBuilder();
        var buffer = new char[Math.Min(4096, maxChars + 1)];
        while (true)
        {
            int n = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
            if (n == 0) return builder.ToString();
            if (builder.Length > maxChars - n)
                throw new InvalidDataException("Native child output exceeded configured limit.");
            builder.Append(buffer, 0, n);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public uint Cb;
        public IntPtr Reserved, Desktop, Title;
        public uint X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public ushort ShowWindow, Reserved2;
        public IntPtr Reserved2Pointer, StdInput, StdOutput, StdError;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx { public StartupInfo StartupInfo; public IntPtr AttributeList; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr ProcessHandle, ThreadHandle;
        public uint ProcessId, ThreadId;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute,
        IntPtr value, IntPtr size, IntPtr previousValue, IntPtr returnSize);
    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(string app, StringBuilder line, IntPtr processAttributes,
        IntPtr threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint flags,
        IntPtr environment, string? directory, ref StartupInfoEx startup, out ProcessInformation result);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr process, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
