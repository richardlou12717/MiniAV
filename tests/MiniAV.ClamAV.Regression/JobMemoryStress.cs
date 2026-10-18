using System.Diagnostics;
using MiniAV.Engines.ClamAV;

internal static class JobMemoryStress
{
    // Manually invoked only. Exercises a harmless child with a 192 MiB Job commit cap.
    private const long TestJobLimit = 192L * 1024 * 1024;
    private const int ChunkSize = 4 * 1024 * 1024;
    private const int TargetChunks = 64; // 256 MiB, safely above the Job limit.

    internal static async Task RunAsync()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows Job Object stress test requires Windows.");
        var dll = typeof(JobMemoryStress).Assembly.Location;
        var info = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        info.ArgumentList.Add(dll);
        info.ArgumentList.Add("--job-memory-worker");
        using var child = Process.Start(info) ?? throw new Exception("Could not start test worker.");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var ready = await child.StandardOutput.ReadLineAsync(timeout.Token);
            if (ready != "JOB_MEMORY_WORKER_READY")
                throw new Exception("Memory worker did not become ready: " + ready);
            // Worker is explicitly paused until attached. This avoids the launch/assignment
            // race for this regression only; the production runner is NOT yet start-suspended.
            using (var job = ClamJob.Attach(child, TestJobLimit))
            {
                await child.StandardInput.WriteLineAsync("GO");
                await child.StandardInput.FlushAsync();
                await child.WaitForExitAsync(timeout.Token);
                var output = await child.StandardOutput.ReadToEndAsync(timeout.Token);
                var error = await child.StandardError.ReadToEndAsync(timeout.Token);
                if (child.ExitCode == 0 || output.Contains("TARGET_REACHED", StringComparison.Ordinal))
                    throw new Exception("FAIL: worker allocated 256 MiB despite 192 MiB Job commit limit.");
                // A nonzero child exit can be a managed OutOfMemoryException or OS termination.
                // This is an observable limit rejection, not proof of aggregate descendant
                // enforcement or of exact maximum memory accounting.
                Console.WriteLine("ClamAV Job Object low-budget memory stress passed (192 MiB cap / 256 MiB target)");
                Console.WriteLine("Worker exit code: " + child.ExitCode);
                if (output.Length > 0) Console.WriteLine("Worker output: " + output.Trim());
                if (error.Length > 0) Console.WriteLine("Worker diagnostics (truncated): " + error[..Math.Min(error.Length, 250)]);
            }
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync();
            }
        }
    }

    internal static void Worker()
    {
        Console.WriteLine("JOB_MEMORY_WORKER_READY");
        Console.Out.Flush();
        if (Console.ReadLine() != "GO")
        {
            Environment.Exit(45);
            return;
        }
        var allocations = new List<byte[]>();
        try
        {
            for (var i = 0; i < TargetChunks; i++)
            {
                var block = GC.AllocateUninitializedArray<byte>(ChunkSize, pinned: true);
                for (var k = 0; k < block.Length; k += 4096) block[k] = 1;
                allocations.Add(block);
            }
            GC.KeepAlive(allocations);
            Console.WriteLine("TARGET_REACHED");
            Environment.Exit(0);
        }
        catch (OutOfMemoryException)
        {
            Console.WriteLine("MEMORY_LIMIT_REACHED");
            Environment.Exit(42);
        }
    }
}
