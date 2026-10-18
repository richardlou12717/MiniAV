namespace MiniAV.Core;

public enum ScanStatus { Matched, NoMatch, Skipped, Error, EngineUnavailable }
public sealed record ScanResult(string Path, ScanStatus Status, string? SignatureId = null,
    string? Category = null, string? Message = null);
public sealed record ScanRequest(string TargetPath, int? MaxFiles = null, long? MaxFileBytes = null, TimeSpan? MaxScanDuration = null);
public sealed record ScanOutcome(int Matched, int NoMatch, int Skipped, int Errors,
    int EngineUnavailable, string SignatureVersion)
{
    public int ExitCode => Errors > 0 || EngineUnavailable > 0 ? 3 : Matched > 0 ? 1 : 0;
}
public interface IScanEngine
{
    string SignatureVersion { get; }
    ValueTask<ScanResult> ScanFileAsync(string file, long? maxFileBytes = null, CancellationToken cancellationToken = default);
}

public sealed class ScanCoordinator(IScanEngine engine)
{
    public async Task<ScanOutcome> ScanAsync(ScanRequest request,
        Action<ScanResult> report, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(report);
        if (request.MaxScanDuration is TimeSpan duration && duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(request), "MaxScanDuration must be positive when specified.");
        if (request.MaxScanDuration is not TimeSpan budget)
            return await ScanInternalAsync(request, report, cancellationToken);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        int matched = 0, noMatch = 0, skipped = 0, errors = 0, unavailable = 0;
        void CountAndReport(ScanResult result)
        {
            switch (result.Status)
            {
                case ScanStatus.Matched: matched++; break;
                case ScanStatus.NoMatch: noMatch++; break;
                case ScanStatus.Skipped: skipped++; break;
                case ScanStatus.Error: errors++; break;
                case ScanStatus.EngineUnavailable: unavailable++; break;
            }
            report(result);
        }
        cancellationToken.ThrowIfCancellationRequested();
        deadline.CancelAfter(budget);
        try
        {
            return await ScanInternalAsync(request, CountAndReport, deadline.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            CountAndReport(new ScanResult(request.TargetPath, ScanStatus.Error, Message: "scan time limit exceeded; scan incomplete"));
            return new ScanOutcome(matched, noMatch, skipped, errors, unavailable, engine.SignatureVersion);
        }
    }

    private async Task<ScanOutcome> ScanInternalAsync(ScanRequest request,
        Action<ScanResult> report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(report);
        if (request.MaxFiles is <= 0)
            throw new ArgumentOutOfRangeException(nameof(request), "MaxFiles must be positive when specified.");
        if (request.MaxFileBytes is <= 0)
            throw new ArgumentOutOfRangeException(nameof(request), "MaxFileBytes must be positive when specified.");
        int filesStarted = 0;
        bool limitReached = false;
        int matched = 0, noMatch = 0, skipped = 0, errors = 0, unavailable = 0;
        void Emit(ScanResult result)
        {
            switch (result.Status)
            {
                case ScanStatus.Matched: matched++; break;
                case ScanStatus.NoMatch: noMatch++; break;
                case ScanStatus.Skipped: skipped++; break;
                case ScanStatus.Error: errors++; break;
                case ScanStatus.EngineUnavailable: unavailable++; break;
            }
            report(result);
        }

        async Task ScanFile(string path)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    Emit(new(path, ScanStatus.Skipped, Message: "reparse point"));
                else
                {
                    if (request.MaxFiles is int maximum && filesStarted >= maximum)
                    {
                        limitReached = true;
                        Emit(new(path, ScanStatus.Error, Message: "scan file limit reached"));
                        return;
                    }
                    if (request.MaxFileBytes is long maxBytes && new FileInfo(path).Length > maxBytes)
                    {
                        Emit(new(path, ScanStatus.Error, Message: "file size limit exceeded"));
                        return;
                    }
                    filesStarted++;
                    var result = await engine.ScanFileAsync(path, request.MaxFileBytes, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    Emit(result);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                Emit(new(path, ScanStatus.Error, Message: e.Message));
            }
        }

        var target = Path.GetFullPath(request.TargetPath);
        if (File.Exists(target))
        {
            await ScanFile(target);
        }
        else if (Directory.Exists(target))
        {
            var pending = new Stack<string>();
            pending.Push(target);
            while (pending.Count > 0 && !limitReached)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var dir = pending.Pop();
                try
                {
                    if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0)
                    {
                        Emit(new(dir, ScanStatus.Skipped, Message: "directory reparse point"));
                        continue;
                    }
                    foreach (var entry in Directory.GetFileSystemEntries(dir))
                    {
                        if (limitReached) break;
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            var attributes = File.GetAttributes(entry);
                            if ((attributes & FileAttributes.ReparsePoint) != 0)
                                Emit(new(entry, ScanStatus.Skipped, Message: "reparse point"));
                            else if ((attributes & FileAttributes.Directory) != 0)
                                pending.Push(entry);
                            else
                                await ScanFile(entry);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
                        {
                            Emit(new(entry, ScanStatus.Error, Message: e.Message));
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
                {
                    Emit(new(dir, ScanStatus.Error, Message: e.Message));
                }
            }
        }
        else throw new FileNotFoundException("Target does not exist.", target);
        cancellationToken.ThrowIfCancellationRequested();
        return new(matched, noMatch, skipped, errors, unavailable, engine.SignatureVersion);
    }
}
