using MiniAV.Core;
using System.Diagnostics;
using System.Runtime.InteropServices;
using MiniAV.Engines.ClamAV;

if (args.Length > 0 && args[0] == "--native-overlapped-pipe-test")
{
    await NativeOverlappedPipeRegression.RunAsync();
    return;
}

if (args.Length > 0 && args[0] == "--suspended-failure-test")
{
    SuspendedLaunchRegression.RunFailureCleanup();
    return;
}
if (args.Length > 0 && args[0] == "--native-failure-repeat-test")
{
    await NativeFailureRepeatRegression.RunAsync();
    return;
}
if (args.Length > 0 && args[0] == "--native-descendant-failure-test")
{
    await NativeDescendantFailureRegression.RunAsync();
    return;
}
if (args.Length > 0 && args[0] == "--native-descendant-cancel-test")
{
    await NativeLauncherRegression.RunDescendantCancellationAsync();
    return;
}
if (args.Length > 0 && args[0] == "--native-active-io-cancel-test")
{
    await NativeLauncherRegression.RunActiveIoCancellationAsync();
    return;
}
if (args.Length > 0 && args[0] == "--native-pre-resume-failure-test")
{
    await NativeLauncherRegression.RunPreResumeFailureAsync();
    return;
}
if (args.Length > 0 && args[0] == "--native-launcher-repeat-test")
{
    await NativeLauncherRegression.RunRepeatedAsync();
    return;
}
if (args.Length > 0 && args[0] == "--native-launcher-test")
{
    await NativeLauncherRegression.RunAsync();
    return;
}
if (args.Length > 0 && args[0] == "--native-io-test")
{
    await NativeInheritedIoRegression.RunAsync();
    return;
}
if (args.Length > 0 && args[0] == "--suspended-launch-test")
{
    await SuspendedLaunchRegression.RunAsync();
    return;
}
if (args.Length > 0 && args[0] == "--job-aggregate-stress")
{
    await JobMemoryStress.RunAggregateAsync();
    return;
}
if (args.Length > 0 && args[0] == "--job-aggregate-parent")
{
    await JobMemoryStress.AggregateParentAsync();
    return;
}
if (args.Length > 0 && args[0] == "--job-aggregate-child")
{
    JobMemoryStress.AggregateChild();
    return;
}
if (args.Length > 0 && args[0] == "--job-memory-stress")
{
    await JobMemoryStress.RunAsync();
    return;
}
if (args.Length > 0 && args[0] == "--job-memory-worker")
{
    JobMemoryStress.Worker();
    return;
}

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
    fake.BeforeReturn = () => File.AppendAllText(file, " changed during scan");
    Require((await engine.ScanFileAsync(file)).Status == ScanStatus.Error, "file mutation cannot be clean");
    fake.BeforeReturn = null;
    Require(fake.Arguments?.Last() == file, "path is a single argument");
    Require(fake.Arguments?.Contains("--infected") == true, "no remediation flag");
    Require(fake.Arguments?.Contains("--alert-exceeds-max=yes") == true, "detect internal limit exclusions");
    Require(fake.Arguments?.Any(x => x.Contains("remove", StringComparison.OrdinalIgnoreCase)) != true, "no delete");
    fake.Next = new(1, file + ": Test.Signature FOUND\n", "");
    var hit = await engine.ScanFileAsync(file);
    Require(hit.Status == ScanStatus.Matched && hit.SignatureId == "Test.Signature", "matched and signature");
    fake.Next = new(1, file + ": Heuristics.Limits.Exceeded FOUND\n", "");
    Require((await engine.ScanFileAsync(file)).Status == ScanStatus.Error, "internal size/recursion limit cannot count as malware or clean");
    fake.Next = new(1, file + ": Heuristics.Limits.Exceeded.MaxFileSize FOUND\n", "");
    Require((await engine.ScanFileAsync(file)).Status == ScanStatus.Error, "specific internal limit variant incomplete");
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
    // Exercise the real subprocess boundary with harmless Windows shell commands.
    // Do not require ClamAV installation or load any virus signatures.
    if (OperatingSystem.IsWindows())
    {
        var cmd = Environment.GetEnvironmentVariable("ComSpec") ?? @"C:\Windows\System32\cmd.exe";
        var runner = new ClamProcessRunner();
        var ok = await runner.RunAsync(cmd, new[] { "/d", "/c", "echo READY" },
            TimeSpan.FromSeconds(10), CancellationToken.None);
        Require(ok.ExitCode == 0 && ok.Stdout.Contains("READY"), "real child stdout/exit");
        try
        {
            await runner.RunAsync(cmd, new[] { "/d", "/c", "ping -n 10 127.0.0.1 >NUL" },
                TimeSpan.FromMilliseconds(250), CancellationToken.None);
            throw new Exception("FAIL: process timeout not enforced");
        }
        catch (TimeoutException) { }
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        try
        {
            await runner.RunAsync(cmd, new[] { "/d", "/c", "ping -n 10 127.0.0.1 >NUL" },
                TimeSpan.FromSeconds(20), cancel.Token);
            throw new Exception("FAIL: process cancel not enforced");
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        try
        {
            await runner.RunAsync(cmd,
                new[] { "/d", "/c", "for /L %i in (1,1,10000) do @echo 1234567890123456789012345678901234567890" },
                TimeSpan.FromSeconds(20), CancellationToken.None);
            throw new Exception("FAIL: process output cap not enforced");
        }
        catch (ProcessOutputLimitException) { }
        // Directly verify Job membership and KILL_ON_JOB_CLOSE; no ClamAV needed.
        using (var held = Process.Start(new ProcessStartInfo(cmd)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { "/d", "/c", "ping -n 25 127.0.0.1 >NUL" }
        }) ?? throw new Exception("FAIL: could not start Job fixture"))
        {
            try
            {
                using (var job = ClamJob.Attach(held, 512L * 1024 * 1024))
                {
                    Require(NativeJobTest.IsProcessInJob(held.Handle, IntPtr.Zero, out var inJob) && inJob,
                        "process assigned to a Windows Job");
                    Require(!held.HasExited, "test process alive before Job close");
                }
                using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await held.WaitForExitAsync(wait.Token);
                Require(held.HasExited, "Job close terminates worker");
            }
            finally
            {
                if (!held.HasExited)
                {
                    held.Kill(entireProcessTree: true);
                    await held.WaitForExitAsync();
                }
            }
        }
        var argv = new[] { "a file 中文.txt", "quote\"inside", @"C:\folder\", "", @"--database=C:\Clam DB" };
        var commandLine = WindowsArgumentEncoding.Build(@"C:\Program Files\ClamAV\clamscan.exe", argv);
        var nativeArgs = NativeCommandLineTest.Parse(commandLine);
        Require(nativeArgs.SequenceEqual(new[] { @"C:\Program Files\ClamAV\clamscan.exe" }.Concat(argv)),
            "Windows native command line quoting and Unicode round-trip");
        try
        {
            WindowsArgumentEncoding.Quote("bad\0arg");
            throw new Exception("FAIL: NUL argument accepted");
        }
        catch (ArgumentException) { }
        Console.WriteLine("Windows CreateProcess argument quoting regressions passed");
        NativePipeRegression.Run();
        await SuspendedLaunchRegression.RunAsync();
        SuspendedLaunchRegression.RunFailureCleanup();
        Console.WriteLine("ClamAV Job Object membership and close regressions passed");
        Console.WriteLine("ClamAV real subprocess lifecycle regressions passed");
    }
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
    public Action? BeforeReturn { get; set; }
    public IReadOnlyList<string>? Arguments { get; private set; }
    public Task<ProcessExecution> RunAsync(string executable, IReadOnlyList<string> arguments,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Arguments = arguments.ToArray();
        if (Failure is not null) return Task.FromException<ProcessExecution>(Failure);
        BeforeReturn?.Invoke();
        return Task.FromResult(Next);
    }
}

internal static class NativeJobTest
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    internal static extern bool IsProcessInJob(IntPtr process, IntPtr job,
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] out bool inJob);
}
