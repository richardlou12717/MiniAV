using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.ExceptionServices;

namespace MiniAV.Engines.ClamAV;

/// <summary>
/// Windows Job Object lifetime + per-process and aggregate Job committed-memory ceilings.
/// Note: assignment happens immediately after Process.Start, not atomically at creation.
/// This is NOT a sandbox, and does not provide a hard CPU-time limit.
/// </summary>
internal sealed class ClamJob : IDisposable
{
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private const uint JobObjectLimitProcessMemory = 0x00000100;
    private const uint JobObjectLimitJobMemory = 0x00000200;
    private const int JobObjectExtendedLimitInformation = 9;
    private IntPtr handle;

    private ClamJob(IntPtr job) => handle = job;

    // Test-only deliberately unaligned nonzero value, not a Windows pseudo-handle
    // (-1 denotes the current process). Never attach a live child to this fixture.
    internal static ClamJob FromInvalidHandleForTest() => new(new IntPtr(0x12345));

    public static ClamJob Attach(Process process, long maxProcessMemoryBytes)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Job Object resource bounds require Windows.");
        if (maxProcessMemoryBytes <= 0 || IntPtr.Size != 8)
            throw new ArgumentOutOfRangeException(nameof(maxProcessMemoryBytes));

        var job = CreateJobObjectW(IntPtr.Zero, null);
        if (job == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        var scope = new ClamJob(job);
        try
        {
            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                {
                    LimitFlags = JobObjectLimitKillOnJobClose | JobObjectLimitProcessMemory | JobObjectLimitJobMemory
                },
                ProcessMemoryLimit = new UIntPtr((ulong)maxProcessMemoryBytes),
                JobMemoryLimit = new UIntPtr((ulong)maxProcessMemoryBytes)
            };
            if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation,
                ref info, (uint)Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>()))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to set ClamAV memory limit.");
            if (!AssignProcessToJobObject(job, process.Handle))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to place ClamAV process in Job Object.");
            return scope;
        }
        catch (Exception attachFailure)
        {
            try { scope.Dispose(); }
            catch (Exception closeFailure)
            {
                throw new AggregateException(
                    "Job attachment failed and its handle could not be closed.",
                    attachFailure, closeFailure);
            }
            ExceptionDispatchInfo.Capture(attachFailure).Throw();
            throw;
        }
    }

    public void Dispose()
    {
        var h = Interlocked.Exchange(ref handle, IntPtr.Zero);
        if (h == IntPtr.Zero) return;
        // Ownership is consumed before the native call: after a failed close the
        // handle's state is ambiguous, and retrying could close a reused handle.
        if (!CloseHandle(h))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Closing the ClamAV Job Object handle failed; descendant termination is not confirmed.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObjectW(IntPtr securityAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(IntPtr hJob, int infoClass,
        ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
