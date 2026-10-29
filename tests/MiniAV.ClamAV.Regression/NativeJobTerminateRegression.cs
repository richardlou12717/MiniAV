using System.ComponentModel;
using System.Diagnostics;
using MiniAV.Engines.ClamAV;

// Isolated proof of explicit termination while the Job handle is still live.
// This is NOT a simulation of a failed first CloseHandle.
internal static class NativeJobTerminateRegression
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
        }) ?? throw new Exception("Could not start harmless Job worker.");
        try
        {
            using var job = ClamJob.Attach(child, 512L * 1024 * 1024);
            if (child.HasExited)
                throw new Exception("Job member exited before explicit termination.");
            if (job.GetActiveProcessCount() == 0)
                throw new Exception("Live Job member was missing from accounting.");
            job.TerminateMembers();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
            if (!child.HasExited)
                throw new Exception("Explicit Job termination failed to stop the worker.");
            // Observe the Job itself, not only the direct child. Do this before
            // disposing the only owned handle; a failed query must fail the test.
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (job.GetActiveProcessCount() != 0 && DateTime.UtcNow < deadline)
                await Task.Delay(50);
            if (job.GetActiveProcessCount() != 0)
                throw new Exception("Job process-tree quiescence was not confirmed.");
            job.Dispose(); // Independent CloseHandle still required.
            Console.WriteLine("M3.3 explicit native Job termination before close passed");
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
            }
        }
    }
}
