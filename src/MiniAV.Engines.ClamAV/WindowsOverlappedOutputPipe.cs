using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MiniAV.Engines.ClamAV;

/// <summary>
/// Experimental M3.1 overlapped parent reader and inheritable child writer.
/// Not yet used by WindowsSuspendedLauncher or the production scanner.
/// </summary>
internal sealed class WindowsOverlappedOutputPipe : IDisposable
{
    private const uint HandleFlagInherit = 1;
    private readonly NamedPipeServerStream _parent;
    private readonly NamedPipeClientStream _child;

    internal Stream Parent => _parent;
    internal SafePipeHandle Child => _child.SafePipeHandle;

    private WindowsOverlappedOutputPipe(NamedPipeServerStream parent, NamedPipeClientStream child)
    {
        _parent = parent;
        _child = child;
    }

    internal static WindowsOverlappedOutputPipe Create()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        // The random name is only a rendezvous identifier, not an authorization mechanism.
        // CurrentUserOnly applies an OS identity restriction to this test-only pipe.
        var name = "miniav-m3-output-" + Guid.NewGuid().ToString("N");
        var parent = new NamedPipeServerStream(name, PipeDirection.In, 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        NamedPipeClientStream? child = null;
        try
        {
            var accepted = parent.WaitForConnectionAsync();
            child = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.None);
            child.Connect(5000);
            accepted.GetAwaiter().GetResult();
            if (!SetHandleInformation(child.SafePipeHandle, HandleFlagInherit, HandleFlagInherit))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Could not mark native child writer inheritable.");
            return new WindowsOverlappedOutputPipe(parent, child);
        }
        catch
        {
            child?.Dispose();
            parent.Dispose();
            throw;
        }
    }

    internal void CloseChildWriter() => _child.Dispose();

    public void Dispose()
    {
        _child.Dispose();
        _parent.Dispose();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetHandleInformation(SafePipeHandle handle, uint mask, uint flags);
}
