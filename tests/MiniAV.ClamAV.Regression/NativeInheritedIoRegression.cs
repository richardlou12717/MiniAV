using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using MiniAV.Engines.ClamAV;

// Opt-in Windows native I/O prototype. Does not change production scanner.
internal static class NativeInheritedIoRegression
{
    private const uint CreateSuspended = 0x00000004;
    private const uint CreateNoWindow = 0x08000000;
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint StartfUseStdHandles = 0x00000100;
    private const int ProcThreadAttributeHandleList = 0x00020002;

    internal static async Task RunAsync()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var stdin = WindowsChildPipe.Create(childReads: true);
        using var stdout = WindowsChildPipe.Create(childReads: false);
        using var stderr = WindowsChildPipe.Create(childReads: false);
        var handles = new[] { stdin.Child.DangerousGetHandle(), stdout.Child.DangerousGetHandle(), stderr.Child.DangerousGetHandle() };
        IntPtr attributes = IntPtr.Zero, handleList = IntPtr.Zero;
        bool initialized = false;
        ProcessInformation native = default;
        try
        {
            IntPtr size = IntPtr.Zero;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
            if (size == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            attributes = Marshal.AllocHGlobal(size);
            if (!InitializeProcThreadAttributeList(attributes, 1, 0, ref size))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            initialized = true;
            handleList = Marshal.AllocHGlobal(handles.Length * IntPtr.Size);
            for (var i = 0; i < handles.Length; i++)
                Marshal.WriteIntPtr(handleList, i * IntPtr.Size, handles[i]);
            if (!UpdateProcThreadAttribute(attributes, 0, (IntPtr)ProcThreadAttributeHandleList,
                handleList, (IntPtr)(handles.Length * IntPtr.Size), IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var cmd = Environment.GetEnvironmentVariable("ComSpec") ?? @"C:\Windows\System32\cmd.exe";
            var line = new StringBuilder(WindowsArgumentEncoding.Build(cmd,
                new[] { "/d", "/s", "/c", "echo NATIVE_STDOUT & echo NATIVE_STDERR 1>&2" }));
            var startup = new StartupInfoEx
            {
                StartupInfo = new StartupInfo
                {
                    Cb = (uint)Marshal.SizeOf<StartupInfoEx>(),
                    Flags = StartfUseStdHandles,
                    StdInput = handles[0], StdOutput = handles[1], StdError = handles[2]
                },
                AttributeList = attributes
            };
            if (!CreateProcessW(cmd, line, IntPtr.Zero, IntPtr.Zero, true,
                CreateSuspended | CreateNoWindow | ExtendedStartupInfoPresent,
                IntPtr.Zero, null, ref startup, out native))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            using var child = Process.GetProcessById(checked((int)native.ProcessId));
            using var job = ClamJob.Attach(child, 128L * 1024 * 1024);
            if (!NativeJobTest.IsProcessInJob(child.Handle, IntPtr.Zero, out var member) || !member)
                throw new Exception("Child not in Job before ResumeThread.");
            using var outputStream = new FileStream(stdout.Parent, FileAccess.Read, 4096, false);
            using var errorStream = new FileStream(stderr.Parent, FileAccess.Read, 4096, false);
            using var output = new StreamReader(outputStream);
            using var error = new StreamReader(errorStream);
            if (ResumeThread(native.ThreadHandle) != 1)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "ResumeThread failed");
            // Close all child endpoints in the parent so stdout/stderr readers can see EOF.
            stdin.Child.Dispose();
            stdout.Child.Dispose();
            stderr.Child.Dispose();
            stdin.Parent.Dispose();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var outTask = output.ReadToEndAsync(deadline.Token);
            var errTask = error.ReadToEndAsync(deadline.Token);
            await Task.WhenAll(child.WaitForExitAsync(deadline.Token), outTask, errTask);
            if (child.ExitCode != 0 || !outTask.Result.Contains("NATIVE_STDOUT", StringComparison.Ordinal)
                || !errTask.Result.Contains("NATIVE_STDERR", StringComparison.Ordinal))
                throw new Exception($"Native redirected IO mismatch: exit={child.ExitCode}; stdout={System.Text.Json.JsonSerializer.Serialize(outTask.Result)}; stderr={System.Text.Json.JsonSerializer.Serialize(errTask.Result)}");
            Console.WriteLine("Windows STARTUPINFOEX redirected child IO regression passed");
        }
        finally
        {
            if (native.ProcessHandle != IntPtr.Zero)
            {
                if (GetExitCodeProcess(native.ProcessHandle, out var state) && state == 259)
                {
                    TerminateProcess(native.ProcessHandle, 91);
                    WaitForSingleObject(native.ProcessHandle, 5000);
                }
                CloseHandle(native.ThreadHandle);
                CloseHandle(native.ProcessHandle);
            }
            if (initialized) DeleteProcThreadAttributeList(attributes);
            if (handleList != IntPtr.Zero) Marshal.FreeHGlobal(handleList);
            if (attributes != IntPtr.Zero) Marshal.FreeHGlobal(attributes);
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
    private struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public IntPtr AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr ProcessHandle, ThreadHandle;
        public uint ProcessId, ThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr list, int attributeCount, int flags, ref IntPtr size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute,
        IntPtr value, IntPtr size, IntPtr previousValue, IntPtr returnSize);

    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr list);

    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(string application, StringBuilder commandLine,
        IntPtr processAttributes, IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint flags,
        IntPtr environment, string? currentDirectory, ref StartupInfoEx startup, out ProcessInformation information);

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
