using MiniAV.Core;
using MiniAV.Engines.ClamAV;

// Explicit local-only real ClamAV test. Never selected by the production CLI.
internal static class NativeRealClamSmokeRegression
{
    internal static async Task RunAsync(string executable, string? database)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (!Path.IsPathFullyQualified(executable) ||
            !string.Equals(Path.GetFileName(executable), "clamscan.exe", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(executable))
            throw new ArgumentException("An existing absolute clamscan.exe path is required.");
        if (database is not null && (!Path.IsPathFullyQualified(database) || !Directory.Exists(database)))
            throw new ArgumentException("Database directory must be an existing absolute directory.");

        var scratch = Path.Combine(Path.GetTempPath(), "miniav-native-real-clam-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var sample = Path.Combine(scratch, "harmless sample with spaces.txt");
            await File.WriteAllTextAsync(sample, "Harmless MiniAV native ClamAV integration fixture. Not a malware sample.");
            var engine = new ClamAvScanEngine(executable, database,
                processTimeout: TimeSpan.FromMinutes(3),
                processRunner: new NativeClamProcessRunnerPrototype());
            var result = await engine.ScanFileAsync(sample);
            if (result.Status != ScanStatus.NoMatch)
                throw new Exception($"Native real ClamAV smoke incomplete: status={result.Status}, message={result.Message}. Do not treat this as clean.");
            Console.WriteLine("M3.3 real clamscan via isolated suspended native adapter passed (harmless file only)");
            Console.WriteLine("ClamAV signature freshness and complete malware detection coverage are NOT verified.");
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }
}
