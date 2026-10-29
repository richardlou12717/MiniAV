using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using MiniAV.Core;

namespace MiniAV.Engines.ClamAV;

/// <summary>Single-file, opt-in ClamAV adapter. Does not update signatures or modify targets.</summary>
public sealed class ClamAvScanEngine : IScanEngine
{
    private readonly IClamProcessRunner runner;
    private readonly string executable;
    private readonly string? database;
    private readonly TimeSpan timeout;

    public string SignatureVersion => "ClamAV (database version not verified)";

    public ClamAvScanEngine(string executablePath, string? databaseDirectory = null,
        TimeSpan? processTimeout = null, IClamProcessRunner? processRunner = null)
    {
        if (!Path.IsPathFullyQualified(executablePath) ||
            !string.Equals(Path.GetFileName(executablePath), "clamscan.exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("An absolute clamscan.exe path is required.", nameof(executablePath));
        if (databaseDirectory is not null && !Path.IsPathFullyQualified(databaseDirectory))
            throw new ArgumentException("Database directory must be absolute.", nameof(databaseDirectory));
        executable = Path.GetFullPath(executablePath);
        database = databaseDirectory is null ? null : Path.GetFullPath(databaseDirectory);
        timeout = processTimeout ?? TimeSpan.FromMinutes(3);
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(processTimeout));
        runner = processRunner ?? new ClamProcessRunner();
    }

    public async ValueTask<ScanResult> ScanFileAsync(string file, long? maxFileBytes = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var path = Path.GetFullPath(file);
            if (path.Any(char.IsControl))
                return new(path, ScanStatus.Error, Message: "File path has unsupported control characters.");
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                return new(path, ScanStatus.Skipped, Message: "reparse point");
            if (maxFileBytes is <= 0) throw new ArgumentOutOfRangeException(nameof(maxFileBytes));
            var before = new FileInfo(path);
            var initialLength = before.Length;
            var initialModifiedUtc = before.LastWriteTimeUtc;
            if (maxFileBytes is long limit && initialLength > limit)
                return new(path, ScanStatus.Error, Message: "file size limit exceeded");
            if (!File.Exists(executable))
                return new(path, ScanStatus.EngineUnavailable, Message: "ClamAV executable not found.");
            if (database is not null && !Directory.Exists(database))
                return new(path, ScanStatus.EngineUnavailable, Message: "ClamAV database directory not found.");

            var args = new List<string> { "--no-summary", "--infected", "--alert-exceeds-max=yes",
                "--follow-dir-symlinks=0", "--follow-file-symlinks=0" };
            if (database is not null) args.Add("--database=" + database);
            args.Add("--");
            args.Add(path);

            ProcessExecution execution;
            try
            {
                execution = await runner.RunAsync(executable, args, timeout, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (TimeoutException)
            {
                return new(path, ScanStatus.Error, Message: "ClamAV process timeout; scan incomplete.");
            }
            catch (ProcessOutputLimitException)
            {
                return new(path, ScanStatus.Error, Message: "ClamAV output exceeded limit; scan incomplete.");
            }
            catch (Exception e) when (e is Win32Exception or FileNotFoundException or UnauthorizedAccessException)
            {
                return new(path, ScanStatus.EngineUnavailable, Message: "ClamAV process could not start.");
            }

            // A file may change after coordinator's preflight and while ClamAV reads it.
            // Metadata comparison detects common mutations; it is not an atomic snapshot.
            var after = new FileInfo(path);
            if (after.Length != initialLength || after.LastWriteTimeUtc != initialModifiedUtc)
                return new(path, ScanStatus.Error, Message: "File changed during external scan; scan incomplete.");
            return Parse(path, execution);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return new(file, ScanStatus.Error, Message: e.Message);
        }
    }

    public static ScanResult Parse(string path, ProcessExecution execution)
    {
        // Any diagnostic, limit warning, or unexpected output makes the result incomplete.
        if (!string.IsNullOrWhiteSpace(execution.Stderr))
            return new(path, ScanStatus.Error, Message: "ClamAV reported diagnostics; scan incomplete.");
        var lines = execution.Stdout.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (execution.ExitCode == 0 && lines.Length == 0)
            return new(path, ScanStatus.NoMatch);
        if (execution.ExitCode == 1 && lines.Length == 1)
        {
            var prefix = path + ": ";
            var line = lines[0];
            if (line.StartsWith(prefix, StringComparison.Ordinal) &&
                line.EndsWith(" FOUND", StringComparison.Ordinal))
            {
                var name = line.Substring(prefix.Length, line.Length - prefix.Length - " FOUND".Length);
                if (name.StartsWith("Heuristics.Limits.Exceeded", StringComparison.OrdinalIgnoreCase))
                    return new(path, ScanStatus.Error, Message: "ClamAV internal scan limit exceeded; scan incomplete.");
                if (name.Length is > 0 and <= 256 && !name.Any(char.IsControl))
                    return new(path, ScanStatus.Matched, SignatureId: name, Category: "ClamAV");
            }
        }
        return new(path, ScanStatus.Error, Message: "ClamAV returned an error or unexpected scan output; scan incomplete.");
    }
}

public sealed record ProcessExecution(int ExitCode, string Stdout, string Stderr);

/// <summary>Injectable process boundary for deterministic tests; never executes scanned content.</summary>
public interface IClamProcessRunner
{
    Task<ProcessExecution> RunAsync(string executable, IReadOnlyList<string> arguments,
        TimeSpan timeout, CancellationToken cancellationToken);
}

public sealed class ProcessOutputLimitException : Exception
{
    public ProcessOutputLimitException() : base("ClamAV process output exceeded configured limit.") { }
}

public sealed class ClamProcessRunner : IClamProcessRunner
{
    private const int MaxCapturedCharacters = 32_768;
    private const long MaxClamProcessMemoryBytes = 2L * 1024 * 1024 * 1024; // 2 GiB private committed memory

    public async Task<ProcessExecution> RunAsync(string executable, IReadOnlyList<string> arguments,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(executable)!
        };
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = start };
        cancellationToken.ThrowIfCancellationRequested();
        if (!process.Start()) throw new Win32Exception("Failed to start ClamAV.");
        ClamJob job;
        try { job = ClamJob.Attach(process, MaxClamProcessMemoryBytes); }
        catch
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (Win32Exception) { }
            await process.WaitForExitAsync(CancellationToken.None);
            throw; // Never run an unbounded child if Job Object assignment fails.
        }
        using var ownedJob = job;
        process.StandardInput.Close();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        // Both pipes are drained concurrently to avoid a blocked child.
        void StopOnOverflow()
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (Win32Exception) { }
        }
        var stdout = ReadBoundedAsync(process.StandardOutput, deadline.Token, StopOnOverflow);
        var stderr = ReadBoundedAsync(process.StandardError, deadline.Token, StopOnOverflow);
        try
        {
            // Watch readers and child together: an overflowing pipe must abort the child.
            await Task.WhenAll(process.WaitForExitAsync(deadline.Token), stdout, stderr);
            return new ProcessExecution(process.ExitCode, await stdout, await stderr);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw new TimeoutException("ClamAV process timeout.");
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { } // exited concurrently
                catch (Win32Exception) { } // child already exited or inaccessible
                await process.WaitForExitAsync(CancellationToken.None);
            }
            // Never turn a primary reader/timeout exception into a clean result.
            try { await Task.WhenAll(stdout, stderr); }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
            catch (ProcessOutputLimitException) { }
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken, Action stopOnOverflow)
    {
        var buffer = new char[1024];
        var output = new StringBuilder();
        while (true)
        {
            int read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) return output.ToString();
            if (output.Length + read > MaxCapturedCharacters)
            {
                stopOnOverflow(); // kill immediately: otherwise the child may block on the full pipe
                throw new ProcessOutputLimitException();
            }
            output.Append(buffer, 0, read);
        }
    }
}
