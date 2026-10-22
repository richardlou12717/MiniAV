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
    // Aggregate test: first worker holds 112 MiB; its descendant tries 160 MiB.
    // Each allocation is below the 256 MiB per-process cap, but the combined
    // allocations exceed the 256 MiB Job cap. All allocations are test-only.
    internal static async Task RunAggregateAsync()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var dll = typeof(JobMemoryStress).Assembly.Location;
        var psi = NewWorkerStart(dll, "--job-aggregate-parent");
        using var parent = Process.Start(psi) ?? throw new Exception("Cannot start aggregate fixture.");
        try
        {
            if (await parent.StandardOutput.ReadLineAsync(timeout.Token) != "AGGREGATE_READY")
                throw new Exception("Aggregate fixture did not start.");
            using var job = ClamJob.Attach(parent, 256L * 1024 * 1024);
            await parent.StandardInput.WriteLineAsync("GO");
            await parent.StandardInput.FlushAsync();
            var result = await parent.StandardOutput.ReadLineAsync(timeout.Token);
            if (result != "AGGREGATE_LIMIT_REACHED")
                throw new Exception("FAIL: aggregate limit was not demonstrated: " + result);
            await parent.WaitForExitAsync(timeout.Token);
            if (parent.ExitCode != 0) throw new Exception("Aggregate fixture exited unexpectedly: " + parent.ExitCode);
            Console.WriteLine("ClamAV Job Object aggregate memory stress passed (256 MiB Job limit; parent 112 MiB + descendant 160 MiB)");
        }
        finally
        {
            if (!parent.HasExited)
            {
                parent.Kill(entireProcessTree: true);
                await parent.WaitForExitAsync();
            }
        }
    }

    private static ProcessStartInfo NewWorkerStart(string dll, string mode)
    {
        var info = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        info.ArgumentList.Add(dll);
        info.ArgumentList.Add(mode);
        return info;
    }

    private static List<byte[]> AllocateMiB(int amount)
    {
        var blocks = new List<byte[]>();
        for (int i = 0; i < amount / 4; i++)
        {
            var block = GC.AllocateUninitializedArray<byte>(ChunkSize, pinned: true);
            for (var j = 0; j < block.Length; j += 4096) block[j] = 1;
            blocks.Add(block);
        }
        return blocks;
    }

    internal static async Task AggregateParentAsync()
    {
        Console.WriteLine("AGGREGATE_READY");
        if (Console.ReadLine() != "GO") return;
        using var child = Process.Start(NewWorkerStart(typeof(JobMemoryStress).Assembly.Location, "--job-aggregate-child"))
            ?? throw new Exception("Could not create descendant");
        try
        {
            if (await child.StandardOutput.ReadLineAsync() != "CHILD_READY") throw new Exception("Child not ready");
            // Child remains idle while parent commits and holds its memory.
            var retained = AllocateMiB(112);
            await child.StandardInput.WriteLineAsync("GO");
            await child.StandardInput.FlushAsync();
            var response = await child.StandardOutput.ReadLineAsync();
            GC.KeepAlive(retained);
            if (response == "CHILD_ALLOCATION_REJECTED")
            {
                Console.WriteLine("AGGREGATE_LIMIT_REACHED");
                return;
            }
            Console.WriteLine("AGGREGATE_UNEXPECTED: " + response);
            Environment.ExitCode = 1;
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

    internal static void AggregateChild()
    {
        Console.WriteLine("CHILD_READY");
        if (Console.ReadLine() != "GO") return;
        try
        {
            var held = AllocateMiB(160);
            GC.KeepAlive(held);
            Console.WriteLine("CHILD_TARGET_REACHED");
        }
        catch (OutOfMemoryException)
        {
            Console.WriteLine("CHILD_ALLOCATION_REJECTED");
        }
    }

}
