using MiniAV.Core;
using MiniAV.Engines.Hash;

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

    var sizeEvents = new List<ScanResult>();
    var sizeLimited = await coordinator.ScanAsync(
        new ScanRequest(file, MaxFileBytes: 1), sizeEvents.Add);
    if (sizeLimited.Errors != 1 || sizeLimited.ExitCode != 3 ||
        sizeEvents.Count != 1 || sizeEvents[0].Status != ScanStatus.Error ||
        sizeEvents[0].Message != "file size limit exceeded")
        throw new Exception("Oversized file must fail closed without hashing.");
    var sizeAllowed = await coordinator.ScanAsync(
        new ScanRequest(file, MaxFileBytes: new FileInfo(file).Length), _ => { });
    if (sizeAllowed.NoMatch != 1 || sizeAllowed.ExitCode != 0)
        throw new Exception("File at exact size boundary should scan.");
    try
    {
        await coordinator.ScanAsync(new ScanRequest(file, MaxFileBytes: 0), _ => { });
        throw new Exception("Expected MaxFileBytes validation failure");
    }
    catch (ArgumentOutOfRangeException) { }
    // Exercise the streaming enforcement directly (rather than only the coordinator preflight).
    var dbFile = Path.Combine(root, "empty-signatures.json");
    await File.WriteAllTextAsync(dbFile, """{"version":"test","signatures":[]}""");
    var hashEngine = HashScanEngine.Load(dbFile);
    var atLimit = await hashEngine.ScanFileAsync(file, new FileInfo(file).Length);
    var overLimit = await hashEngine.ScanFileAsync(file, new FileInfo(file).Length - 1);
    var unbounded = await hashEngine.ScanFileAsync(file);
    if (atLimit.Status != ScanStatus.NoMatch || unbounded.Status != ScanStatus.NoMatch ||
        overLimit.Status != ScanStatus.Error || overLimit.Message != "file size limit exceeded")
        throw new Exception("Hash engine actual-read byte limit failed.");
    Console.WriteLine("Core file-size regression tests passed");

    var timeoutEvents = new List<ScanResult>();
    var timedOutcome = await new ScanCoordinator(new SlowEngine()).ScanAsync(
        new ScanRequest(file, MaxScanDuration: TimeSpan.FromMilliseconds(50)), timeoutEvents.Add);
    if (timedOutcome.ExitCode != 3 || timedOutcome.Errors != 1 ||
        timeoutEvents.Count != 1 || timeoutEvents[0].Status != ScanStatus.Error ||
        timeoutEvents[0].Message != "scan time limit exceeded; scan incomplete")
        throw new Exception("Deadline must produce an incomplete scan error.");

    using (var userCancel = new CancellationTokenSource())
    {
        userCancel.CancelAfter(TimeSpan.FromMilliseconds(30));
        await ExpectCancelled(() => new ScanCoordinator(new SlowEngine()).ScanAsync(
            new ScanRequest(file, MaxScanDuration: TimeSpan.FromSeconds(5)), _ => { }, userCancel.Token));
    }
    try
    {
        await coordinator.ScanAsync(new ScanRequest(file, MaxScanDuration: TimeSpan.Zero), _ => { });
        throw new Exception("Expected invalid duration rejection");
    }
    catch (ArgumentOutOfRangeException) { }
    Console.WriteLine("Core scan-time regression tests passed");

    // Deterministic close-while-scanning lifecycle test (no timing or UI clicks).
    var closeGate = new ScanCloseGate();
    using (var closeCancellation = new CancellationTokenSource())
    {
        var workerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowWorkerExit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = Task.Run(async () =>
        {
            workerEntered.SetResult();
            await allowWorkerExit.Task;
            closeCancellation.Token.ThrowIfCancellationRequested();
        });
        closeGate.ScanStarted();
        await workerEntered.Task;
        var mayCloseNow = closeGate.RequestClose(closeCancellation.Cancel);
        if (mayCloseNow || !closeCancellation.IsCancellationRequested || worker.IsCompleted)
            throw new Exception("Window close was not deferred while the worker remained active.");
        // The close gate may only complete after the controlled worker has exited.
        allowWorkerExit.SetResult();
        await ExpectCancelled(async () => await worker);
        if (!closeGate.ScanFinished())
            throw new Exception("Pending close was not released after scan exit.");
        if (!closeGate.RequestClose(() => throw new Exception("No cancellation expected after exit")))
            throw new Exception("Window should be permitted to close after scan exit.");
    }
    Console.WriteLine("Core window-close lifecycle regression tests passed");
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
    public ValueTask<ScanResult> ScanFileAsync(string file, long? maxFileBytes = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new ScanResult(file, ScanStatus.NoMatch));
    }
}

sealed class ThrowingEngine(Exception failure) : IScanEngine
{
    public string SignatureVersion => "throwing-stub";
    public ValueTask<ScanResult> ScanFileAsync(string file, long? maxFileBytes = null, CancellationToken cancellationToken = default)
        => ValueTask.FromException<ScanResult>(failure);
}

sealed class SlowEngine : IScanEngine
{
    public string SignatureVersion => "slow-stub";
    public async ValueTask<ScanResult> ScanFileAsync(string file, long? maxFileBytes = null,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return new ScanResult(file, ScanStatus.NoMatch);
    }
}
