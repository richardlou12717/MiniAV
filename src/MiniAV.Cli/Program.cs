using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

// M1 proof-of-concept: read-only scanner; demo signatures are NOT malware intelligence.
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

SignatureDatabase? db;
try
{
    using var input = File.OpenRead(databaseFile);
    db = JsonSerializer.Deserialize<SignatureDatabase>(input);
    if (db is null || db.Signatures is null) throw new InvalidDataException("Empty signature database.");
}
catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
{
    Console.Error.WriteLine($"Cannot load signature DB: {e.Message}");
    return 2;
}

var signatures = new Dictionary<string, DemoSignature>(StringComparer.OrdinalIgnoreCase);
foreach (var item in db.Signatures)
{
    if (item.Sha256 is null || item.Sha256.Length != 64 || !item.Sha256.All(Uri.IsHexDigit))
    {
        Console.Error.WriteLine("Signature DB contains an invalid SHA-256.");
        return 2;
    }
    signatures[item.Sha256] = item;
}

var matched = 0;
var noMatch = 0;
var errors = 0;
var skipped = 0;

void ScanOne(string file)
{
    try
    {
        var attrs = File.GetAttributes(file);
        if ((attrs & FileAttributes.ReparsePoint) != 0)
        {
            Console.WriteLine($"SKIPPED (reparse point) {file}");
            skipped++;
            return;
        }
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1024 * 128, FileOptions.SequentialScan);
        var hash = Convert.ToHexString(SHA256.HashData(stream));
        if (signatures.TryGetValue(hash, out var hit))
        {
            Console.WriteLine($"MATCHED [{hit.Id}: {hit.Category}] {file}");
            matched++;
        }
        else
        {
            Console.WriteLine($"NO_MATCH {file}");
            noMatch++;
        }
    }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
    {
        Console.WriteLine($"ERROR {file}: {e.Message}");
        errors++;
    }
}

if (File.Exists(target)) ScanOne(target);
else
{
    var pending = new Stack<string>();
    pending.Push(target);
    while (pending.Count > 0)
    {
        var dir = pending.Pop();
        try
        {
            if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0)
            {
                Console.WriteLine($"SKIPPED (directory reparse point) {dir}");
                skipped++;
                continue;
            }
            foreach (var entry in Directory.GetFileSystemEntries(dir))
            {
                try
                {
                    var attrs = File.GetAttributes(entry);
                    if ((attrs & FileAttributes.ReparsePoint) != 0)
                    {
                        Console.WriteLine($"SKIPPED (reparse point) {entry}");
                        skipped++;
                    }
                    else if ((attrs & FileAttributes.Directory) != 0) pending.Push(entry);
                    else ScanOne(entry);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
                {
                    Console.WriteLine($"ERROR {entry}: {e.Message}");
                    errors++;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Console.WriteLine($"ERROR {dir}: {e.Message}");
            errors++;
        }
    }
}
Console.WriteLine($"SUMMARY Matched={matched} NoMatch={noMatch} Skipped={skipped} Errors={errors} Database={db.Version}");
return errors > 0 ? 3 : matched > 0 ? 1 : 0;

internal sealed class SignatureDatabase
{
    [JsonPropertyName("version")] public string Version { get; set; } = "unknown";
    [JsonPropertyName("signatures")] public List<DemoSignature>? Signatures { get; set; }
}
internal sealed class DemoSignature
{
    [JsonPropertyName("id")] public string Id { get; set; } = "Unknown";
    [JsonPropertyName("sha256")] public string? Sha256 { get; set; }
    [JsonPropertyName("category")] public string Category { get; set; } = "DemoOnly";
}
