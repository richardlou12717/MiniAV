using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiniAV.Core;

namespace MiniAV.Engines.Hash;

public sealed class HashScanEngine : IScanEngine
{
    private readonly Dictionary<string, DemoSignature> signatures;
    public string SignatureVersion { get; }

    private HashScanEngine(string version, Dictionary<string, DemoSignature> signatures)
    {
        SignatureVersion = version;
        this.signatures = signatures;
    }

    public static HashScanEngine Load(string databaseFile)
    {
        using var input = File.OpenRead(databaseFile);
        var db = JsonSerializer.Deserialize<SignatureDatabase>(input)
            ?? throw new InvalidDataException("Empty signature database.");
        if (db.Signatures is null)
            throw new InvalidDataException("Missing signatures.");
        var signatures = new Dictionary<string, DemoSignature>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in db.Signatures)
        {
            if (item.Sha256 is null || item.Sha256.Length != 64 || !item.Sha256.All(Uri.IsHexDigit))
                throw new InvalidDataException("Signature DB contains an invalid SHA-256.");
            signatures[item.Sha256] = item;
        }
        return new(db.Version, signatures);
    }

    public async ValueTask<ScanResult> ScanFileAsync(string file, CancellationToken cancellationToken = default)
    {
        try
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                return new(file, ScanStatus.Skipped, Message: "reparse point");
            await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
            return signatures.TryGetValue(hash, out var hit)
                ? new(file, ScanStatus.Matched, hit.Id, hit.Category)
                : new(file, ScanStatus.NoMatch);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new(file, ScanStatus.Error, Message: e.Message);
        }
    }

    private sealed class SignatureDatabase
    {
        [JsonPropertyName("version")] public string Version { get; set; } = "unknown";
        [JsonPropertyName("signatures")] public List<DemoSignature>? Signatures { get; set; }
    }
    private sealed class DemoSignature
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "Unknown";
        [JsonPropertyName("sha256")] public string? Sha256 { get; set; }
        [JsonPropertyName("category")] public string Category { get; set; } = "DemoOnly";
    }
}
