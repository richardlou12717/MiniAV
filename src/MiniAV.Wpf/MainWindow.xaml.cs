using System.IO;
using System.Collections.Concurrent;
using System.Text;
using System.Windows.Threading;
using System.Windows;
using Microsoft.Win32;
using MiniAV.Core;
using MiniAV.Engines.Hash;
using MiniAV.Engines.ClamAV;

namespace MiniAV.Wpf;

public partial class MainWindow : Window
{
    private CancellationTokenSource? scanCancellation;
    private readonly ScanCloseGate closeGate = new();
    private int processed, matched, noMatch, skipped, errors;
    private readonly BlockingCollection<ScanResult> pendingResults = new(new ConcurrentQueue<ScanResult>(), 2000);
    private bool logTruncated;
    private const int MaxDisplayCharacters = 500_000;
    private readonly DispatcherTimer refreshTimer;
    private const int RefreshBatchSize = 400;

    private void FlushPendingResults()
    {
        var text = new StringBuilder();
        int count = 0;
        while (count < RefreshBatchSize && pendingResults.TryTake(out var result))
        {
            count++;
            processed++;
            switch (result.Status)
            {
                case ScanStatus.Matched: matched++; break;
                case ScanStatus.NoMatch: noMatch++; break;
                case ScanStatus.Skipped: skipped++; break;
                default: errors++; break;
            }
            var label = result.Status switch
            {
                ScanStatus.Matched => $"MATCHED [{result.SignatureId}]",
                ScanStatus.NoMatch => "NO_MATCH",
                ScanStatus.Skipped => "SKIPPED",
                _ => "ERROR"
            };
            text.Append(label).Append(' ').Append(result.Path).Append(' ')
                .Append(result.Message).AppendLine();
        }
        if (count == 0) return;
        var chunk = text.ToString();
        if (chunk.Length > MaxDisplayCharacters / 2)
        {
            logTruncated = true;
            chunk = chunk[^(MaxDisplayCharacters / 2)..];
        }
        if (ResultsBox.Text.Length + chunk.Length > MaxDisplayCharacters)
        {
            logTruncated = true;
            ResultsBox.Clear();
            ResultsBox.AppendText("[Earlier scan details omitted from display due to memory limit]" + Environment.NewLine);
        }
        ResultsBox.AppendText(chunk);
        ResultsBox.ScrollToEnd();
        UpdateCounts();
    }

    private async Task FlushAllPendingAsync()
    {
        while (pendingResults.Count > 0)
        {
            FlushPendingResults();
            await Dispatcher.Yield(DispatcherPriority.Background);
        }
    }


    private void UpdateCounts() => CountText.Text = $"Processed: {processed} | Matched: {matched} | No match: {noMatch} | Skipped: {skipped} | Errors: {errors}";

    private void BrowseFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Choose file to scan", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) TargetBox.Text = dialog.FileName;
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose folder to scan" };
        if (dialog.ShowDialog(this) == true) TargetBox.Text = dialog.FolderName;
    }

    private void EngineBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (DatabaseBox is null || ClamExecutableBox is null || ClamDatabaseBox is null) return;
        var hash = EngineBox.SelectedIndex != 1;
        DatabaseBox.IsEnabled = hash;
        BrowseDatabaseButton.IsEnabled = hash;
        ClamExecutableBox.IsEnabled = !hash;
        ClamDatabaseBox.IsEnabled = !hash;
        BrowseClamExecutableButton.IsEnabled = !hash;
        BrowseClamDatabaseButton.IsEnabled = !hash;
    }

    private void BrowseClamExecutable_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Choose clamscan.exe", Filter = "ClamAV scanner (clamscan.exe)|clamscan.exe" };
        if (dialog.ShowDialog(this) == true) ClamExecutableBox.Text = dialog.FileName;
    }

    private void BrowseClamDatabase_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose ClamAV signature directory" };
        if (dialog.ShowDialog(this) == true) ClamDatabaseBox.Text = dialog.FolderName;
    }

    private void BrowseDatabase_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Choose demo signature database", Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) == true) DatabaseBox.Text = dialog.FileName;
    }

    private void SaveLog_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Title = "Save MiniAV scan report", Filter = "Text logs (*.txt)|*.txt", FileName = "MiniAV-scan-report.txt" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var header = logTruncated
                ? "PARTIAL DISPLAY LOG - earlier results omitted. Not a complete scan report." + Environment.NewLine
                : "";
            File.WriteAllText(dialog.FileName, header + ResultsBox.Text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            MessageBox.Show(this, ex.Message, "Cannot save report", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public MainWindow()
    {
        InitializeComponent();
        refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        refreshTimer.Tick += (_, _) => FlushPendingResults();
        TargetBox.Text = Path.Combine(Environment.CurrentDirectory, "tests", "fixtures");
        DatabaseBox.Text = Path.Combine(Environment.CurrentDirectory, "signatures", "demo-hashes.json");
        EngineBox_SelectionChanged(this, null!);
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        var target = TargetBox.Text.Trim();
        var dbPath = DatabaseBox.Text.Trim();
        if (!File.Exists(target) && !Directory.Exists(target))
        {
            MessageBox.Show(this, "Target does not exist.", "MiniAV", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Validate all user-supplied limits before starting the worker.
        static bool TryPositive(string input, out long? value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(input)) return true;
            if (!long.TryParse(input, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var number) || number <= 0)
                return false;
            value = number;
            return true;
        }

        if (!TryPositive(MaxFilesBox.Text.Trim(), out var filesValue) ||
            filesValue is > int.MaxValue ||
            !TryPositive(MaxFileMiBBox.Text.Trim(), out var mibValue) ||
            mibValue is > (long.MaxValue / (1024L * 1024L)) ||
            !TryPositive(MaxDurationBox.Text.Trim(), out var secondsValue) ||
            secondsValue is > 4294967L)
        {
            MessageBox.Show(this, "Limits must be positive whole numbers within the supported range, or blank for unlimited.",
                "Invalid scan limit", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var request = new ScanRequest(target,
            MaxFiles: filesValue is long maxFiles ? (int)maxFiles : null,
            MaxFileBytes: mibValue is long maxMiB ? maxMiB * 1024L * 1024L : null,
            MaxScanDuration: secondsValue is long seconds ? TimeSpan.FromSeconds(seconds) : null);

        IScanEngine engine;
        if (EngineBox.SelectedIndex == 1)
        {
            try
            {
                var clamDatabase = ClamDatabaseBox.Text.Trim();
                engine = new ClamAvScanEngine(ClamExecutableBox.Text.Trim(),
                    string.IsNullOrWhiteSpace(clamDatabase) ? null : clamDatabase);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                MessageBox.Show(this, ex.Message, "Invalid ClamAV configuration", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        else
        {
            try { engine = HashScanEngine.Load(dbPath); }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException or NotSupportedException)
            {
                MessageBox.Show(this, ex.Message, "Cannot load signature database", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        closeGate.ScanStarted();
        scanCancellation = new CancellationTokenSource();
        var token = scanCancellation.Token;
        processed = matched = noMatch = skipped = errors = 0;
        UpdateCounts();
        while (pendingResults.TryTake(out _)) { }
        logTruncated = false;
        ResultsBox.Clear();
        refreshTimer.Start();
        SaveLogButton.IsEnabled = false;
        StartButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        TargetBox.IsEnabled = false;
        DatabaseBox.IsEnabled = false;
        EngineBox.IsEnabled = ClamExecutableBox.IsEnabled = ClamDatabaseBox.IsEnabled = false;
        BrowseClamExecutableButton.IsEnabled = BrowseClamDatabaseButton.IsEnabled = false;
        MaxFilesBox.IsEnabled = MaxFileMiBBox.IsEnabled = MaxDurationBox.IsEnabled = false;
        BrowseFileButton.IsEnabled = BrowseFolderButton.IsEnabled = BrowseDatabaseButton.IsEnabled = false;
        BusyBar.Visibility = Visibility.Visible;
        StatusText.Text = EngineBox.SelectedIndex == 1 ? "Scanning using ClamAV (database freshness unverified)..." : "Scanning using demo SHA-256 signatures...";

        try
        {
            // Worker enqueues results without waiting for the UI; a timer batches rendering.
            var outcome = await Task.Run(() => new ScanCoordinator(engine).ScanAsync(
                request, result => pendingResults.Add(result, token), token));
            refreshTimer.Stop();
            await FlushAllPendingAsync();
            StatusText.Text = outcome.Errors > 0 || outcome.EngineUnavailable > 0
                ? $"Finished with errors — scan incomplete (exit {outcome.ExitCode})"
                : $"Finished (exit {outcome.ExitCode})";
            ResultsBox.AppendText($"SUMMARY Matched={outcome.Matched} NoMatch={outcome.NoMatch} Skipped={outcome.Skipped} Errors={outcome.Errors + outcome.EngineUnavailable}{Environment.NewLine}");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            refreshTimer.Stop();
            await FlushAllPendingAsync();
            StatusText.Text = "Cancelled — incomplete scan";
            ResultsBox.AppendText($"CANCELLED — scan incomplete{Environment.NewLine}");
        }
        catch (Exception ex)
        {
            refreshTimer.Stop();
            await FlushAllPendingAsync();
            StatusText.Text = "Scan failed — incomplete scan";
            ResultsBox.AppendText($"ERROR {ex.Message}{Environment.NewLine}");
        }
        finally
        {
            refreshTimer.Stop();
            BusyBar.Visibility = Visibility.Collapsed;
            StartButton.IsEnabled = true;
            CancelButton.IsEnabled = false;
            TargetBox.IsEnabled = true;
            EngineBox.IsEnabled = true;
            EngineBox_SelectionChanged(this, null!);
            MaxFilesBox.IsEnabled = MaxFileMiBBox.IsEnabled = MaxDurationBox.IsEnabled = true;
            BrowseFileButton.IsEnabled = BrowseFolderButton.IsEnabled = true;
            SaveLogButton.IsEnabled = ResultsBox.Text.Length > 0;
            scanCancellation?.Dispose();
            scanCancellation = null;
            if (closeGate.ScanFinished())
                Close();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (scanCancellation is null || scanCancellation.IsCancellationRequested) return;
        CancelButton.IsEnabled = false;
        StatusText.Text = "Cancelling — incomplete scan";
        scanCancellation.Cancel();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!closeGate.RequestClose(() => scanCancellation?.Cancel()))
        {
            e.Cancel = true;
            CancelButton.IsEnabled = false;
            StatusText.Text = "Cancelling scan before exit...";
            return;
        }
        base.OnClosing(e);
    }
}
