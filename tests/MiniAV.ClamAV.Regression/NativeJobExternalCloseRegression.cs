using System.ComponentModel;
using System.Diagnostics;
using MiniAV.Engines.ClamAV;

internal static class NativeJobExternalCloseRegression
{
    internal static async Task RunAsync()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var cmd = Environment.GetEnvironmentVariable("ComSpec") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        using var child = Process.Start(new ProcessStartInfo(cmd)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { "/d", "/c", "ping -n 30 127.0.0.1 >NUL" }
        }) ?? throw new Exception("Cannot create harmless Job member.");
        ClamJob? job = null;
        try
        {
            job = ClamJob.Attach(child, 512L * 1024 * 1024);
            if (child.HasExited) throw new Exception("Job member exited before close.");
            // This first close genuinely succeeds and should terminate the member.
            // Dispose then encounters ERROR_INVALID_HANDLE on the stale wrapper.
            job.CloseExternallyForTest();
            try
            {
                job.Dispose();
                throw new Exception("Dispose accepted externally closed Job handle.");
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 6) { }
            job.Dispose(); // No retry of stale handle.
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
            if (!child.HasExited) throw new Exception("Job member survived successful external Job close.");
            Console.WriteLine("M3.3 external successful Job close, live member termination, and stale-handle error passed");
        }
        finally
        {
            // Avoid obscuring test errors, but do not leave a child behind.
            try { job?.Dispose(); } catch (Win32Exception) { }
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
            }
        }
    }
}
