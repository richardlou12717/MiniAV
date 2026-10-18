using System.IO;
using System.Windows;
using MiniAV.Core;
using MiniAV.Engines.Hash;

namespace MiniAV.Wpf;

public partial class MainWindow : Window
{
    private CancellationTokenSource? scanCancellation;
    private int processed;

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
        processed = 0;
        CountText.Text = "Processed: 0";
        ResultsBox.Clear();
        StartButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        TargetBox.IsEnabled = false;
        DatabaseBox.IsEnabled = false;
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
                        CountText.Text = $"Processed: {processed}";
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
            scanCancellation.Dispose();
            scanCancellation = null;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => scanCancellation?.Cancel();

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (scanCancellation is not null)
        {
            e.Cancel = true;
            scanCancellation.Cancel();
            StatusText.Text = "Cancelling scan before exit...";
            return;
        }
        base.OnClosing(e);
    }
}
