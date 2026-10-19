using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MiniAV.Engines.ClamAV;

/// <summary>Owns a Windows anonymous pipe with only its designated child end inheritable.</summary>
internal sealed class WindowsChildPipe : IDisposable
{
    private const uint HandleFlagInherit = 0x00000001;
    internal SafeFileHandle Parent { get; }
    internal SafeFileHandle Child { get; }

    private WindowsChildPipe(SafeFileHandle parent, SafeFileHandle child)
    {
        Parent = parent;
        Child = child;
    }

    internal static WindowsChildPipe Create(bool childReads)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var security = new SecurityAttributes
        {
            Length = (uint)Marshal.SizeOf<SecurityAttributes>(),
            InheritHandle = true
        };
        if (!CreatePipe(out var read, out var write, ref security, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create child pipe.");
        var parent = childReads ? write : read;
        var child = childReads ? read : write;
        try
        {
            if (!SetHandleInformation(parent, HandleFlagInherit, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not clear parent handle inheritance.");
            return new WindowsChildPipe(parent, child);
        }
        catch
        {
            read.Dispose();
            write.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        Parent.Dispose();
        Child.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public uint Length;
        public IntPtr SecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)] public bool InheritHandle;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreatePipe(out SafeFileHandle readPipe, out SafeFileHandle writePipe,
        ref SecurityAttributes attributes, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetHandleInformation(SafeFileHandle handle, uint mask, uint flags);
}
