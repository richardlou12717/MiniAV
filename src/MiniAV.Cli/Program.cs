using System.Text.Json;
using MiniAV.Core;
using MiniAV.Engines.Hash;
using MiniAV.Engines.ClamAV;

// Backward compatible: --path <target> --db <demo.json> defaults to demo hash.
// ClamAV is strictly opt-in: --engine clamav --clamav-exe <absolute clamscan.exe>
// Optional: --clamav-db <absolute database directory>. No implicit downloads.
const string usage = "Usage: MiniAV.Cli --path <file-or-dir> [--engine hash --db <demo-signatures.json> | --engine clamav --clamav-exe <absolute-clamscan.exe> [--clamav-db <absolute-directory>]]";
if (args.Length == 0 || args.Length % 2 != 0)
{
    Console.Error.WriteLine(usage);
    return 2;
}
var options = new Dictionary<string, string>(StringComparer.Ordinal);
for (var i = 0; i < args.Length; i += 2)
{
    if (args[i] is not ("--path" or "--engine" or "--db" or "--clamav-exe" or "--clamav-db") ||
        !options.TryAdd(args[i], args[i + 1]) || string.IsNullOrWhiteSpace(args[i + 1]))
    {
        Console.Error.WriteLine(usage);
        return 2;
    }
}
if (!options.TryGetValue("--path", out var rawTarget))
{
    Console.Error.WriteLine(usage);
    return 2;
}
string target;
try { target = Path.GetFullPath(rawTarget); }
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
IScanEngine engine;
var kind = options.GetValueOrDefault("--engine", "hash");
if (kind == "hash")
{
    if (!options.TryGetValue("--db", out var rawDb) ||
        options.ContainsKey("--clamav-exe") || options.ContainsKey("--clamav-db"))
    {
        Console.Error.WriteLine(usage);
        return 2;
    }
    try
    {
        engine = HashScanEngine.Load(Path.GetFullPath(rawDb));
    }
    catch (Exception e) when (e is ArgumentException or IOException or InvalidDataException or UnauthorizedAccessException or JsonException or NotSupportedException)
    {
        Console.Error.WriteLine($"Cannot load signature DB: {e.Message}");
        return 2;
    }
}
else if (kind == "clamav")
{
    if (!options.TryGetValue("--clamav-exe", out var executable) || options.ContainsKey("--db"))
    {
        Console.Error.WriteLine(usage);
        return 2;
    }
    try
    {
        engine = new ClamAvScanEngine(executable, options.GetValueOrDefault("--clamav-db"));
    }
    catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
    {
        Console.Error.WriteLine($"Invalid ClamAV configuration: {e.Message}");
        return 2;
    }
    Console.Error.WriteLine("ClamAV is optional; signature freshness and complete scan coverage are not verified.");
}
else
{
    Console.Error.WriteLine(usage);
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
