namespace MiniAV.Engines.ClamAV;

/// <summary>
/// M3.3 opt-in compatibility prototype. Never selected by ClamAvScanEngine's
/// default constructor. Production remains on ClamProcessRunner until accepted.
/// </summary>
internal sealed class NativeClamProcessRunnerPrototype : IClamProcessRunner
{
    private const int MaxCapturedCharacters = 32_768;
    private const long MaxProcessMemoryBytes = 2L * 1024 * 1024 * 1024;

    public async Task<ProcessExecution> RunAsync(string executable,
        IReadOnlyList<string> arguments, TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await WindowsSuspendedLauncher.RunAsync(
                executable, arguments, Path.GetDirectoryName(executable),
                MaxProcessMemoryBytes, MaxCapturedCharacters,
                timeout, cancellationToken,
                useOverlappedOutputPipes: true).ConfigureAwait(false);
            return new ProcessExecution(result.ExitCode,
                result.StandardOutput, result.StandardError);
        }
        catch (InvalidDataException ex) when (ex.Message == "Native child output exceeded configured limit.")
        {
            // Translate ONLY an isolated output-cap exception, not arbitrary
            // cleanup/IO faults or AggregateException double faults.
            throw new ProcessOutputLimitException();
        }
    }
}
