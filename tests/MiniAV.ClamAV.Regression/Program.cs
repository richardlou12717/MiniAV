using MiniAV.Core;
using MiniAV.Engines.ClamAV;

static void Require(bool condition, string label)
{
    if (!condition) throw new Exception("FAIL: " + label);
}

var temp = Path.Combine(Path.GetTempPath(), "miniav-clamav-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    var executable = Path.Combine(temp, "clamscan.exe");
    var file = Path.Combine(temp, "a file 中文.txt");
    await File.WriteAllTextAsync(executable, "harmless fake executable marker");
    await File.WriteAllTextAsync(file, "harmless fixture");
    var fake = new FakeRunner();
    var engine = new ClamAvScanEngine(executable, processRunner: fake);
    Require(engine.SignatureVersion.Contains("not verified", StringComparison.OrdinalIgnoreCase), "version is not invented");

    fake.Next = new(0, "", "");
    Require((await engine.ScanFileAsync(file)).Status == ScanStatus.NoMatch, "clean");
    Require(fake.Arguments?.Last() == file, "path is a single argument");
    Require(fake.Arguments?.Contains("--infected") == true, "no remediation flag");
    Require(fake.Arguments?.Any(x => x.Contains("remove", StringComparison.OrdinalIgnoreCase)) != true, "no delete");
    fake.Next = new(1, file + ": Test.Signature FOUND\n", "");
    var hit = await engine.ScanFileAsync(file);
    Require(hit.Status == ScanStatus.Matched && hit.SignatureId == "Test.Signature", "matched and signature");
    fake.Next = new(1, "", "");
    Require((await engine.ScanFileAsync(file)).Status == ScanStatus.Error, "missing detection text");
    fake.Next = new(1, "OTHER: Test FOUND\n", "");
    Require((await engine.ScanFileAsync(file)).Status == ScanStatus.Error, "spoofed file");
    fake.Next = new(0, "unexpected diagnostic\n", "");
    Require((await engine.ScanFileAsync(file)).Status == ScanStatus.Error, "unexpected clean output");
    fake.Next = new(0, "", "warning");
    Require((await engine.ScanFileAsync(file)).Status == ScanStatus.Error, "diagnostic on exit zero");
    fake.Next = new(2, "", "");
    Require((await engine.ScanFileAsync(file)).Status == ScanStatus.Error, "engine error");
    fake.Next = new(77, "", "");
    Require((await engine.ScanFileAsync(file)).Status == ScanStatus.Error, "unexpected exit code");
    fake.Next = new(1, file + ": Name FOUND\n" + file + ": Another FOUND\n", "");
    Require((await engine.ScanFileAsync(file)).Status == ScanStatus.Error, "multiple lines rejected");
    fake.Failure = new TimeoutException();
    Require((await engine.ScanFileAsync(file)).Status == ScanStatus.Error, "local timeout");
    fake.Failure = new ProcessOutputLimitException();
    Require((await engine.ScanFileAsync(file)).Status == ScanStatus.Error, "bounded output failure");
    fake.Failure = null;
    var missing = new ClamAvScanEngine(Path.Combine(temp, "missing", "clamscan.exe"), processRunner: fake);
    Require((await missing.ScanFileAsync(file)).Status == ScanStatus.EngineUnavailable, "missing executable");
    Require((await engine.ScanFileAsync(file, 1)).Status == ScanStatus.Error, "file preflight limit");
    using var cts = new CancellationTokenSource();
    cts.Cancel();
    try
    {
        await engine.ScanFileAsync(file, cancellationToken: cts.Token);
        throw new Exception("FAIL: cancellation did not throw");
    }
    catch (OperationCanceledException) { }
    Require(ClamAvScanEngine.Parse(file, new(0, "", "")).Status == ScanStatus.NoMatch, "parser clean");
    Console.WriteLine("ClamAV fake-runner regression tests passed");
}
finally
{
    Directory.Delete(temp, recursive: true);
}

sealed class FakeRunner : IClamProcessRunner
{
    public ProcessExecution Next { get; set; } = new(0, "", "");
    public Exception? Failure { get; set; }
    public IReadOnlyList<string>? Arguments { get; private set; }
    public Task<ProcessExecution> RunAsync(string executable, IReadOnlyList<string> arguments,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Arguments = arguments.ToArray();
        if (Failure is not null) return Task.FromException<ProcessExecution>(Failure);
        return Task.FromResult(Next);
    }
}
