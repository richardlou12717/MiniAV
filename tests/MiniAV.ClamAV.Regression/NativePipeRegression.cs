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
            // Exercise each direction with small harmless binary data before closing handles.
            // No child process is launched in this fixture.
            var expected = new byte[] { 0, 1, 2, 127, 128, 255 };
            using var writer = new FileStream(childReads ? pipe.Parent : pipe.Child, FileAccess.Write, 4096, false);
            using var reader = new FileStream(childReads ? pipe.Child : pipe.Parent, FileAccess.Read, 4096, false);
            writer.Write(expected);
            writer.Flush();
            var actual = new byte[expected.Length];
            reader.ReadExactly(actual);
            if (!actual.AsSpan().SequenceEqual(expected))
                throw new Exception("FAIL: pipe payload round-trip mismatch");
        }
        Console.WriteLine("Windows child pipe inheritance and byte-transfer regressions passed");
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
