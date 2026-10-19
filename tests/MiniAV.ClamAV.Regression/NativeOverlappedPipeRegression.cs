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
        Console.WriteLine("M3.1 pipe stage: write");
        await pipe.ChildWriter.WriteAsync(bytes).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await pipe.ChildWriter.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var received = new byte[bytes.Length];
        Console.WriteLine("M3.1 pipe stage: read bytes");
        await pipe.Parent.ReadExactlyAsync(received).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
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
