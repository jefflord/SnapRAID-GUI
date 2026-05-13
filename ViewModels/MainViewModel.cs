namespace SnapRAIDGUI.ViewModels;

using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SnapRAIDGUI.Models;
using SnapRAIDGUI.Services;
using Microsoft.Win32;

public partial class MainViewModel : BaseViewModel
{
    private readonly SnapRAIDService _snapRAIDService = new();
    private readonly LoggingService _loggingService = new("logs");
    private readonly ConfigService _configService = new();

    [ObservableProperty] private string _consoleOutput = string.Empty;
    [ObservableProperty] private StatusData? _statusData;
    [ObservableProperty] private DiffData? _diffData;
    [ObservableProperty] private bool _isRunningOperation;
    [ObservableProperty] private string _operationStatus = "Ready";
    [ObservableProperty] private string _logDirectoryPath = string.Empty;

    public RelayCommand SaveSettingsCommand { get; }
    public RelayCommand BrowseSnapRaidExeCommand { get; }
    public RelayCommand BrowseConfFileCommand { get; }

    [RelayCommand] private void Status() => _ = OnStatus();
    [RelayCommand(CanExecute = nameof(CanRunOperation))] private void Diff() => _ = OnDiff();
    [RelayCommand(CanExecute = nameof(CanRunOperation))] private void Sync() => _ = OnSync();
    [RelayCommand(CanExecute = nameof(CanRunOperation))] private void ScrubNew() => _ = OnScrub(false);
    [RelayCommand(CanExecute = nameof(CanRunOperation))] private void ScrubFull() => _ = OnScrub(true);
    [RelayCommand(CanExecute = nameof(CanRunOperation))] private void Fix() => OnFix();
    [RelayCommand(CanExecute = nameof(CanRunOperation))] private void Smart() => RunAsync(() => _snapRAIDService.RunSmartAsync());
    [RelayCommand(CanExecute = nameof(IsRunningOperation))] private void Cancel() => _snapRAIDService.Cancel();
    [RelayCommand] private void OpenSettings() => ShowSettings();

    [ObservableProperty] private bool _showSettingsDialog;
    [ObservableProperty] private SnapRAIDSettings? _settings;

    public MainViewModel()
    {
        SaveSettingsCommand = new RelayCommand(SaveSettingsImpl);
        BrowseSnapRaidExeCommand = new RelayCommand(BrowseSnapRaidExe);
        BrowseConfFileCommand = new RelayCommand(BrowseConfFile);

        _snapRAIDService.OutputReceived += OnOutputReceived;
        _snapRAIDService.ExitCodeReceived += OnExitCodeReceived;
        _snapRAIDService.ErrorOccurred += OnErrorOccurred;

        Settings = LoadSettingsOrDefault();

        // Pass settings to the service so it uses the correct snapraid.exe path
        _snapRAIDService.SetSettings(Settings);

        // Set log directory path for display in console and status bar
        LogDirectoryPath = _loggingService.LogDirectory;

        // Write startup message with log folder location
        var startupMsg = $"=== SnapRAID GUI Started ===\n";
        startupMsg += $"snapraid.exe: {Settings?.SnapRaidExePath ?? "not configured"}\n";
        startupMsg += $"snapraid.conf: {Settings?.ConfFilePath ?? "not configured"}\n";
        startupMsg += $"Logs directory: {_loggingService.LogDirectory}\n";
        startupMsg += $"===========================\n\n";
        AppendConsole(startupMsg);

        // Log the startup event
        _loggingService.WriteLog("startup", startupMsg);

        // Auto-open settings if snapraid.exe path is not configured or file doesn't exist
        var needsConfig = string.IsNullOrWhiteSpace(Settings?.SnapRaidExePath) || !File.Exists(Settings.SnapRaidExePath);
        if (needsConfig)
        {
            ShowSettingsDialog = true;
        }
    }

    private bool CanRunOperation() => !IsRunningOperation;

    private SnapRAIDSettings LoadSettingsOrDefault()
    {
        try { return _configService.LoadSettings(); }
        catch { return new SnapRAIDSettings(); }
    }

    private void SaveSettingsImpl()
    {
        if (Settings != null)
        {
            _configService.SaveSettings(Settings);
            // Update the service with new settings so it uses correct paths
            _snapRAIDService.SetSettings(Settings);
            ShowSettingsDialog = false;
            // Update all command states after settings change
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    private void ShowSettings()
    {
        Settings ??= LoadSettingsOrDefault();
        ShowSettingsDialog = true;
    }

    private void BrowseSnapRaidExe()
    {
        var dlg = new OpenFileDialog { Filter = "Executable Files|*.exe", Title = "Select snapraid.exe" };
        if (dlg.ShowDialog() == true) Settings!.SnapRaidExePath = dlg.FileName;
    }

    private void BrowseConfFile()
    {
        var dlg = new OpenFileDialog { Filter = "Config Files|*.conf|All Files|*.*", Title = "Select snapraid.conf" };
        if (dlg.ShowDialog() == true) Settings!.ConfFilePath = dlg.FileName;
    }

    private async Task OnSync()
    {
        if (!ValidateSnapRaidPath()) return;

        ClearConsole();
        AppendConsole("Running safety check (diff)...\n");

        var diffOutput = await _snapRAIDService.RunDiffAsync();
        DiffData = DiffParser.Parse(diffOutput);

        if (DiffData.HasLargeDeletions(Settings?.DeletionWarningThreshold ?? 50))
        {
            var result = System.Windows.MessageBox.Show(
                $"Large number of deletions detected ({DiffData.RemovedFiles} files removed).\n\n" +
                $"Are you sure you want to update parity?\n\n" +
                $"Threshold: {Settings?.DeletionWarningThreshold ?? 50}",
                "Warning: Large Deletions Detected",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes;

            if (!result) { AppendConsole("Sync cancelled by user.\n"); return; }
        }

        ClearConsole();
        AppendConsole("Starting sync...\n");
        IsRunningOperation = true;
        OperationStatus = "Sync in progress...";

        var output = await _snapRAIDService.RunSyncAsync();
        _loggingService.WriteLog("sync", output);
    }

    private async Task OnScrub(bool full)
    {
        if (!ValidateSnapRaidPath()) return;

        var mode = full ? "Full" : "New";
        ClearConsole();
        AppendConsole($"Starting {mode} scrub...\n");
        IsRunningOperation = true;
        OperationStatus = $"Scrub ({mode}) in progress...";

        var output = await _snapRAIDService.RunScrubAsync(full);
        _loggingService.WriteLog("scrub", output);
    }

    private void OnFix()
    {
        if (!ValidateSnapRaidPath()) return;

        var result = System.Windows.MessageBox.Show(
            "The fix command will revert files to their last synced state.\n\n" +
            "This is a destructive operation that may overwrite recent changes.\n\n" +
            "Are you sure you want to proceed?",
            "Confirm Fix Operation",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes;

        if (!result) return;

        ClearConsole();
        AppendConsole("Starting fix...\n");
        IsRunningOperation = true;
        OperationStatus = "Fix in progress...";

        Task.Run(async () =>
        {
            var output = await _snapRAIDService.RunFixAsync();
            _loggingService.WriteLog("fix", output);
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                IsRunningOperation = false;
                OperationStatus = "Ready";
            });
        });
    }

    private async Task OnDiff()
    {
        if (!ValidateSnapRaidPath()) return;

        ClearConsole();
        AppendConsole("Running diff...\n");
        IsRunningOperation = true;
        OperationStatus = "Diff in progress...";

        var output = await _snapRAIDService.RunDiffAsync();
        DiffData = DiffParser.Parse(output);
        _loggingService.WriteLog("diff", output);

        if (string.IsNullOrEmpty(DiffData?.RawOutput) || !DiffData.RawOutput.Contains("added:", StringComparison.OrdinalIgnoreCase))
        {
            AppendConsole("\n[INFO] No changes detected — array is in sync.\n");
        }
        else
        {
            AppendConsole($"\n--- Diff Summary ---\n");
            AppendConsole($"  Added:   {DiffData.AddedFiles}\n");
            AppendConsole($"  Removed: {DiffData.RemovedFiles}\n");
            AppendConsole($"  Updated: {DiffData.UpdatedFiles}\n");
            AppendConsole($"  Moved:   {DiffData.MovedFiles}\n");
            AppendConsole($"  Copied:  {DiffData.CopiedFiles}\n");
            AppendConsole($"  Equal:   {DiffData.EqualFiles}\n");
            AppendConsole("--------------------\n\n");

            if (DiffData.HasLargeDeletions(Settings?.DeletionWarningThreshold ?? 50))
            {
                AppendConsole($"[WARNING] Large deletions detected! Use Sync to update parity.\n");
            }
        }

        IsRunningOperation = false;
        OperationStatus = "Ready";
    }

    private async Task OnStatus()
    {
        if (!ValidateSnapRaidPath()) return;

        ClearConsole();
        AppendConsole("Fetching status...\n");
        IsRunningOperation = true;
        OperationStatus = "Refreshing...";

        var output = await _snapRAIDService.RunStatusAsync();
        StatusData = StatusParser.Parse(output);
        _loggingService.WriteLog("status", output);

        if (StatusData != null)
        {
            AppendConsole($"\n--- Status Summary ---\n");
            AppendConsole($"  Parity fragmentation: {StatusData.ParityFragmentationPercent}%\n");
            AppendConsole($"  Array status:         {StatusData.ArrayAgeStatus}\n");
            AppendConsole($"  Days since sync:      {StatusData.DaysSinceLastSync}\n");
            AppendConsole($"  Drives detected:      {StatusData.Drives.Count}\n");
            if (StatusData.BadBlockDrives.Any())
                AppendConsole($"  Bad block drives:     {string.Join(", ", StatusData.BadBlockDrives)}\n");
            AppendConsole("----------------------\n\n");
        }

        IsRunningOperation = false;
        OperationStatus = "Ready";
    }

    private bool ValidateSnapRaidPath()
    {
        if (string.IsNullOrWhiteSpace(Settings?.SnapRaidExePath))
        {
            ShowSettings();
            return false;
        }
        if (!File.Exists(Settings.SnapRaidExePath))
        {
            AppendConsole($"[ERROR] snapraid.exe not found at:\n{Settings.SnapRaidExePath}\n\nPlease set the correct path in Settings.\n");
            ShowSettings();
            return false;
        }
        return true;
    }

    private void RunAsync(Func<Task> action)
    {
        Task.Run(async () => await action()).ContinueWith(t =>
        {
            if (t.IsFaulted && t.Exception != null)
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    AppendConsole($"[ERROR] {t.Exception.GetBaseException().Message}\n"));
            }
        }, TaskScheduler.Default);
    }

    private void AppendConsole(string text)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() => ConsoleOutput += text);
    }

    private void ClearConsole()
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() => ConsoleOutput = string.Empty);
    }

    private void OnOutputReceived(object? sender, string output)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() => AppendConsole(output));
    }

    private void OnExitCodeReceived(object? sender, int code) { /* update status if needed */ }

    private void OnErrorOccurred(object? sender, string error)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() => AppendConsole($"[ERROR] {error}\n"));
    }

    public void RefreshDashboard()
    {
        // Don't try to refresh if snapraid.exe path isn't configured or valid
        if (string.IsNullOrWhiteSpace(Settings?.SnapRaidExePath) || !File.Exists(Settings.SnapRaidExePath))
            return;

        RunAsync(async () =>
        {
            try
            {
                var output = await _snapRAIDService.RunStatusAsync();
                if (!string.IsNullOrEmpty(output) && !output.Contains("[ERROR]", StringComparison.OrdinalIgnoreCase))
                    StatusData = StatusParser.Parse(output);
            }
            catch (Exception ex)
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    AppendConsole($"[ERROR] Failed to refresh status: {ex.Message}\n"));
            }
        });
    }
}
