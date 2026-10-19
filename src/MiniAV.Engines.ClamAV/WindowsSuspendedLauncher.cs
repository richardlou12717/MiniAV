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
    private const uint StillActive = 259;
    internal sealed record Result(int ExitCode, string StandardOutput, string StandardError);

    internal static async Task<Result> RunAsync(string executable, IReadOnlyList<string> args,
        string? workingDirectory, long jobMemoryLimitBytes, int maxCharsPerStream,
        TimeSpan timeout, CancellationToken cancellationToken)
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
        using var stdout = WindowsChildPipe.Create(childReads: false);
        using var stderr = WindowsChildPipe.Create(childReads: false);
        var handles = new[] { stdin.Child.DangerousGetHandle(), stdout.Child.DangerousGetHandle(), stderr.Child.DangerousGetHandle() };
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
            token.ThrowIfCancellationRequested();
            if (ResumeThread(native.ThreadHandle) != 1)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "ResumeThread failed.");
            stdin.Child.Dispose();
            stdout.Child.Dispose();
            stderr.Child.Dispose();
            stdin.Parent.Dispose();
            using var stdoutStream = new FileStream(stdout.Parent, FileAccess.Read, 4096, isAsync: false);
            using var stderrStream = new FileStream(stderr.Parent, FileAccess.Read, 4096, isAsync: false);
            using var outReader = new StreamReader(stdoutStream, Encoding.UTF8, true);
            using var errReader = new StreamReader(stderrStream, Encoding.UTF8, true);
            // Drain both streams concurrently; never use unbounded ReadToEndAsync.
            var outTask = ReadBoundedAsync(outReader, maxCharsPerStream, token);
            var errTask = ReadBoundedAsync(errReader, maxCharsPerStream, token);
            var waitTask = child.WaitForExitAsync(token);
            // Do not use Task.WhenAll as the first await: on overflowing output, another
            // synchronous-backed pipe read can remain blocked until Job termination.
            // Observe each completion and return to finally immediately on any failure.
            var pending = new List<Task> { outTask, errTask, waitTask };
            var cancelled = Task.Delay(Timeout.InfiniteTimeSpan, token);
            while (pending.Count != 0)
            {
                var completed = await Task.WhenAny(pending.Append(cancelled)).ConfigureAwait(false);
                if (completed == cancelled)
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
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw new TimeoutException("Native child exceeded its execution deadline.");
        }
        finally
        {
            // Job close is first: kills descendants too, including after a drain overflow.
            job?.Dispose();
            if (native.ProcessHandle != IntPtr.Zero)
            {
                if (GetExitCodeProcess(native.ProcessHandle, out var code) && code == StillActive)
                {
                    TerminateProcess(native.ProcessHandle, 91);
                    var wait = WaitForSingleObject(native.ProcessHandle, 5000);
                    if (wait != 0) Debug.WriteLine($"Native process cleanup wait returned {wait}.");
                }
                CloseHandle(native.ThreadHandle);
                CloseHandle(native.ProcessHandle);
            }
            child?.Dispose();
            if (initialized) DeleteProcThreadAttributeList(attributes);
            if (listMemory != IntPtr.Zero) Marshal.FreeHGlobal(listMemory);
            if (attributes != IntPtr.Zero) Marshal.FreeHGlobal(attributes);
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
