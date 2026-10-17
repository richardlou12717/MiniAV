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
