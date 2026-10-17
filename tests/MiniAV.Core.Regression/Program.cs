using MiniAV.Core;

var root = Path.Combine(Path.GetTempPath(), "miniav-cancellation-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var file = Path.Combine(root, "ordinary.txt");
    await File.WriteAllTextAsync(file, "harmless test content");
    var coordinator = new ScanCoordinator(new StubEngine());

    using (var beforeStart = new CancellationTokenSource())
    {
        beforeStart.Cancel();
        await ExpectCancelled(() => coordinator.ScanAsync(new ScanRequest(file), _ =>
            throw new Exception("Cancelled scan emitted a result"), beforeStart.Token));
    }

    using (var duringReport = new CancellationTokenSource())
    {
        var reports = 0;
        await ExpectCancelled(() => coordinator.ScanAsync(new ScanRequest(file), _ =>
        {
            reports++;
            duringReport.Cancel();
        }, duringReport.Token));
        if (reports != 1)
            throw new Exception($"Expected one callback before cancellation, got {reports}");
    }

    Console.WriteLine("Core cancellation regression tests passed");

    // Simulated I/O and access errors do not require elevated Windows permissions.
    foreach (var failure in new Exception[] { new IOException("simulated I/O failure"), new UnauthorizedAccessException("simulated access denied") })
    {
        var reported = new List<ScanResult>();
        var result = await new ScanCoordinator(new ThrowingEngine(failure))
            .ScanAsync(new ScanRequest(file), reported.Add);
        if (result.Errors != 1 || result.ExitCode != 3 ||
            reported.Count != 1 || reported[0].Status != ScanStatus.Error ||
            !reported[0].Message!.Contains(failure.Message))
            throw new Exception($"Error handling failed for {failure.GetType().Name}");
    }
    Console.WriteLine("Core read-error regression tests passed");

    // Cap scan work and fail closed instead of reporting an incomplete scan as clean.
    var limited = Path.Combine(root, "limited");
    Directory.CreateDirectory(limited);
    await File.WriteAllTextAsync(Path.Combine(limited, "a.txt"), "a");
    await File.WriteAllTextAsync(Path.Combine(limited, "b.txt"), "b");
    var limitedResults = new List<ScanResult>();
    var limitedOutcome = await coordinator.ScanAsync(new ScanRequest(limited, MaxFiles: 1), limitedResults.Add);
    if (limitedOutcome.NoMatch != 1 || limitedOutcome.Errors != 1 || limitedOutcome.ExitCode != 3 ||
        !limitedResults.Any(r => r.Status == ScanStatus.Error && r.Message == "scan file limit reached"))
        throw new Exception("MaxFiles limit failed to stop or signal incomplete scanning.");
    try
    {
        await coordinator.ScanAsync(new ScanRequest(file, MaxFiles: 0), _ => { });
        throw new Exception("Expected MaxFiles validation failure");
    }
    catch (ArgumentOutOfRangeException) { }
    Console.WriteLine("Core scan-limit regression tests passed");
}
finally
{
    Directory.Delete(root, recursive: true);
}

static async Task ExpectCancelled(Func<Task<ScanOutcome>> action)
{
    try
    {
        await action();
    }
    catch (OperationCanceledException)
    {
        return;
    }
    throw new Exception("Expected OperationCanceledException");
}

sealed class StubEngine : IScanEngine
{
    public string SignatureVersion => "stub";
    public ValueTask<ScanResult> ScanFileAsync(string file, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new ScanResult(file, ScanStatus.NoMatch));
    }
}

sealed class ThrowingEngine(Exception failure) : IScanEngine
{
    public string SignatureVersion => "throwing-stub";
    public ValueTask<ScanResult> ScanFileAsync(string file, CancellationToken cancellationToken = default)
        => ValueTask.FromException<ScanResult>(failure);
}
