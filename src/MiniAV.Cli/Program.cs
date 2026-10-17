using System.Text.Json;
using MiniAV.Core;
using MiniAV.Engines.Hash;

if (args.Length != 4 || args[0] != "--path" || args[2] != "--db")
{
    Console.Error.WriteLine("Usage: MiniAV.Cli --path <file-or-dir> --db <demo-signatures.json>");
    return 2;
}

string target;
string databaseFile;
try
{
    target = Path.GetFullPath(args[1]);
    databaseFile = Path.GetFullPath(args[3]);
}
catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
{
    Console.Error.WriteLine($"Invalid path: {e.Message}");
    return 2;
}
if (!File.Exists(target) && !Directory.Exists(target))
{
    Console.Error.WriteLine($"Target does not exist: {target}");
    return 2;
}

HashScanEngine engine;
try
{
    engine = HashScanEngine.Load(databaseFile);
}
catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or NotSupportedException)
{
    Console.Error.WriteLine($"Cannot load signature DB: {e.Message}");
    return 2;
}

using var cancellation = new CancellationTokenSource();
ConsoleCancelEventHandler cancelHandler = (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};
Console.CancelKeyPress += cancelHandler;
try
{
var coordinator = new ScanCoordinator(engine);
var outcome = await coordinator.ScanAsync(new ScanRequest(target), result =>
{
    switch (result.Status)
    {
        case ScanStatus.Matched:
            Console.WriteLine($"MATCHED [{result.SignatureId}: {result.Category}] {result.Path}");
            break;
        case ScanStatus.NoMatch:
            Console.WriteLine($"NO_MATCH {result.Path}");
            break;
        case ScanStatus.Skipped:
            Console.WriteLine($"SKIPPED ({result.Message}) {result.Path}");
            break;
        case ScanStatus.Error:
        case ScanStatus.EngineUnavailable:
            Console.WriteLine($"ERROR {result.Path}: {result.Message}");
            break;
    }
}, cancellation.Token);
Console.WriteLine($"SUMMARY Matched={outcome.Matched} NoMatch={outcome.NoMatch} Skipped={outcome.Skipped} Errors={outcome.Errors + outcome.EngineUnavailable} Database={outcome.SignatureVersion}");
return outcome.ExitCode;
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    Console.Error.WriteLine("Scan cancelled by user.");
    return 130;
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
}
