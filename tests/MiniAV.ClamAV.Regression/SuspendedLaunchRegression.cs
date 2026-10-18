using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using MiniAV.Engines.ClamAV;

// Windows-only test prototype. Not wired into production ClamProcessRunner.
// Proves native suspended creation + Job assignment before the main thread resumes.
internal static class SuspendedLaunchRegression
{
    private const uint CreateSuspended = 0x00000004;
    private const uint CreateNoWindow = 0x08000000;
    private const uint StillActive = 259;

    internal static async Task RunAsync()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var cmd = Environment.GetEnvironmentVariable("ComSpec") ?? @"C:\Windows\System32\cmd.exe";
        var start = new StartupInfo { Cb = (uint)Marshal.SizeOf<StartupInfo>() };
        // The harmless child exits immediately once its primary thread is resumed.
        var line = new StringBuilder("\"" + cmd + "\" /d /c exit 0");
        if (!CreateProcessW(cmd, line, IntPtr.Zero, IntPtr.Zero, false,
            CreateSuspended | CreateNoWindow, IntPtr.Zero, null, ref start, out var native))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CREATE_SUSPENDED failed.");

        try
        {
            using var child = Process.GetProcessById(native.ProcessId);
            // An exit before Job attachment would violate this test's safety invariant.
            if (child.HasExited) throw new Exception("FAIL: suspended process executed before attachment.");
            using var job = ClamJob.Attach(child, 128L * 1024 * 1024);
            if (!NativeJobTest.IsProcessInJob(child.Handle, IntPtr.Zero, out var inJob) || !inJob)
                throw new Exception("FAIL: suspended child is not in a Job.");
            if (child.HasExited) throw new Exception("FAIL: child exited before resume.");
            var result = ResumeThread(native.ThreadHandle);
            if (result == uint.MaxValue || result != 1)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unexpected ResumeThread result.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await child.WaitForExitAsync(timeout.Token);
            if (child.ExitCode != 0) throw new Exception("FAIL: resumed worker returned " + child.ExitCode);
            Console.WriteLine("Suspended-launch Job-before-resume regression passed");
        }
        finally
        {
            // Includes failure before ResumeThread: no indefinitely suspended child.
            if (GetExitCodeProcess(native.ProcessHandle, out var code) && code == StillActive)
                TerminateProcess(native.ProcessHandle, 90);
            CloseHandle(native.ThreadHandle);
            CloseHandle(native.ProcessHandle);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public uint Cb;
        public IntPtr Reserved;
        public IntPtr Desktop;
        public IntPtr Title;
        public uint X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute;
        public uint Flags;
        public ushort ShowWindow, Reserved2;
        public IntPtr Reserved2Pointer;
        public IntPtr StdInput, StdOutput, StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr ProcessHandle;
        public IntPtr ThreadHandle;
        public uint ProcessId;
        public uint ThreadId;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(string application, StringBuilder commandLine,
        IntPtr processAttributes, IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint creationFlags,
        IntPtr environment, string? currentDirectory, ref StartupInfo startup,
        out ProcessInformation processInfo);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr thread);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
