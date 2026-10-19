using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using MiniAV.Engines.ClamAV;

internal static class NativePipeRegression
{
    private const uint HandleFlagInherit = 1;

    internal static void Run()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        foreach (var childReads in new[] { true, false })
        {
            using var pipe = WindowsChildPipe.Create(childReads);
            if (pipe.Parent.IsInvalid || pipe.Child.IsInvalid)
                throw new Exception("FAIL: invalid pipe handles");
            if (IsInheritable(pipe.Parent))
                throw new Exception("FAIL: parent pipe endpoint is inheritable");
            if (!IsInheritable(pipe.Child))
                throw new Exception("FAIL: child pipe endpoint is not inheritable");
        }
        Console.WriteLine("Windows child pipe inheritance regressions passed");
    }

    private static bool IsInheritable(SafeFileHandle handle)
    {
        if (!GetHandleInformation(handle, out var flags))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return (flags & HandleFlagInherit) != 0;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetHandleInformation(SafeFileHandle handle, out uint flags);
}
