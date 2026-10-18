using System.IO;
using System.Windows;
using Microsoft.Win32;
using MiniAV.Core;
using MiniAV.Engines.Hash;

namespace MiniAV.Wpf;

public partial class MainWindow : Window
{
    private CancellationTokenSource? scanCancellation;
    private bool closeAfterCancellation;
    private int processed, matched, noMatch, skipped, errors;

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

    private void BrowseDatabase_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Choose demo signature database", Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) == true) DatabaseBox.Text = dialog.FileName;
    }

    private void SaveLog_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Title = "Save MiniAV scan report", Filter = "Text logs (*.txt)|*.txt", FileName = "MiniAV-scan-report.txt" };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllText(dialog.FileName, ResultsBox.Text); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            MessageBox.Show(this, ex.Message, "Cannot save report", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public MainWindow()
    {
        InitializeComponent();
        TargetBox.Text = Path.Combine(Environment.CurrentDirectory, "tests", "fixtures");
        DatabaseBox.Text = Path.Combine(Environment.CurrentDirectory, "signatures", "demo-hashes.json");
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

        HashScanEngine engine;
        try { engine = HashScanEngine.Load(dbPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or NotSupportedException)
        {
            MessageBox.Show(this, ex.Message, "Cannot load signature database", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        scanCancellation = new CancellationTokenSource();
        var token = scanCancellation.Token;
        processed = matched = noMatch = skipped = errors = 0;
        UpdateCounts();
        ResultsBox.Clear();
        SaveLogButton.IsEnabled = false;
        StartButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        TargetBox.IsEnabled = false;
        DatabaseBox.IsEnabled = false;
        BrowseFileButton.IsEnabled = BrowseFolderButton.IsEnabled = BrowseDatabaseButton.IsEnabled = false;
        BusyBar.Visibility = Visibility.Visible;
        StatusText.Text = "Scanning...";

        try
        {
            // Scanner callbacks run on a worker, and UI updates are marshalled to Dispatcher.
            var outcome = await Task.Run(() => new ScanCoordinator(engine).ScanAsync(
                new ScanRequest(target), result =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        processed++;
                        switch (result.Status)
                        {
                            case ScanStatus.Matched: matched++; break;
                            case ScanStatus.NoMatch: noMatch++; break;
                            case ScanStatus.Skipped: skipped++; break;
                            default: errors++; break;
                        }
                        UpdateCounts();
                        var label = result.Status switch
                        {
                            ScanStatus.Matched => $"MATCHED [{result.SignatureId}]",
                            ScanStatus.NoMatch => "NO_MATCH",
                            ScanStatus.Skipped => "SKIPPED",
                            _ => "ERROR"
                        };
                        ResultsBox.AppendText($"{label} {result.Path} {result.Message}{Environment.NewLine}");
                        ResultsBox.ScrollToEnd();
                    });
                }, token));
            StatusText.Text = $"Finished (exit {outcome.ExitCode})";
            ResultsBox.AppendText($"SUMMARY Matched={outcome.Matched} NoMatch={outcome.NoMatch} Skipped={outcome.Skipped} Errors={outcome.Errors + outcome.EngineUnavailable}{Environment.NewLine}");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            StatusText.Text = "Cancelled — incomplete scan";
            ResultsBox.AppendText($"CANCELLED — scan incomplete{Environment.NewLine}");
        }
        catch (Exception ex)
        {
            StatusText.Text = "Scan failed — incomplete scan";
            ResultsBox.AppendText($"ERROR {ex.Message}{Environment.NewLine}");
        }
        finally
        {
            BusyBar.Visibility = Visibility.Collapsed;
            StartButton.IsEnabled = true;
            CancelButton.IsEnabled = false;
            TargetBox.IsEnabled = true;
            DatabaseBox.IsEnabled = true;
            BrowseFileButton.IsEnabled = BrowseFolderButton.IsEnabled = BrowseDatabaseButton.IsEnabled = true;
            SaveLogButton.IsEnabled = ResultsBox.Text.Length > 0;
            scanCancellation?.Dispose();
            scanCancellation = null;
            if (closeAfterCancellation)
            {
                closeAfterCancellation = false;
                Close();
            }
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
        if (scanCancellation is not null)
        {
            e.Cancel = true;
            closeAfterCancellation = true;
            CancelButton.IsEnabled = false;
            scanCancellation.Cancel();
            StatusText.Text = "Cancelling scan before exit...";
            return;
        }
        base.OnClosing(e);
    }
}
