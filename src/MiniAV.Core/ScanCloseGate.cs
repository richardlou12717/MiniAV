namespace MiniAV.Core;

/// <summary>
/// UI-thread-owned state gate: defer a window close until its active scan has returned.
/// RequestClose never waits synchronously, so the UI can process completion and cancellation.
/// </summary>
public sealed class ScanCloseGate
{
    private bool scanRunning;
    private bool closeRequested;

    public void ScanStarted()
    {
        if (scanRunning) throw new InvalidOperationException("Scan already active.");
        scanRunning = true;
        closeRequested = false;
    }

    public bool RequestClose(Action requestCancellation)
    {
        ArgumentNullException.ThrowIfNull(requestCancellation);
        if (!scanRunning) return true;
        closeRequested = true;
        requestCancellation();
        return false;
    }

    public bool ScanFinished()
    {
        if (!scanRunning) throw new InvalidOperationException("No active scan.");
        scanRunning = false;
        var shouldClose = closeRequested;
        closeRequested = false;
        return shouldClose;
    }
}
