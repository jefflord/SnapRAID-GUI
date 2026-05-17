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
    [ObservableProperty] private System.Collections.ObjectModel.ObservableCollection<MergedDriveEntry> _mergedDrives = new();

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

        try
        {
        var output = await _snapRAIDService.RunStatusAsync();
        StatusData = StatusParser.Parse(output);

        // Log raw status output
        _loggingService.WriteLog("status", output);

        // Log parser diagnostics immediately after parsing
        _loggingService.WriteLog("status_parse_diag", StatusData.ParseDiagnostics);

        // Parse snapraid.conf for individual drive entries
        if (!string.IsNullOrWhiteSpace(Settings?.ConfFilePath) && File.Exists(Settings.ConfFilePath))
        {
            try
            {
                StatusData.ConfigDrives = ConfigParser.Parse(Settings.ConfFilePath);
            }
            catch (Exception ex)
            {
                AppendConsole($"[WARN] Could not parse snapraid.conf: {ex.Message}\n");
            }
        }
        else
        {
            AppendConsole($"[WARN] snapraid.conf not found or path empty: '{Settings?.ConfFilePath}'\n");
        }

        BuildMergedDrives(StatusData);

        if (StatusData != null)
        {
            var summary = new System.Text.StringBuilder();
            summary.AppendLine("\n--- Status Summary ---");
            summary.AppendLine($"  Parity fragmentation: {StatusData.ParityFragmentationPercent}%");
            summary.AppendLine($"  Array status:         {StatusData.ArrayAgeStatus}");
            summary.AppendLine($"  Days since sync:      {StatusData.DaysSinceLastSync}");
            summary.AppendLine($"  Scrub status:         {StatusData.ScrubStatus}");
            summary.AppendLine($"  Drives from status:   {StatusData.Drives.Count}");
            foreach (var d in StatusData.Drives)
                summary.AppendLine($"    [{d.Type}] {d.Name}  used={d.UsedSizeBytes / 1073741824.0:F1}GB  total={d.TotalSizeBytes / 1073741824.0:F1}GB  fill={d.FillPercent:F1}%");
            summary.AppendLine($"  Conf drives:          {StatusData.ConfigDrives.Count}");
            foreach (var c in StatusData.ConfigDrives)
                summary.AppendLine($"    [{c.Type}] {c.Name}  {c.Path}");
            summary.AppendLine($"  Merged drive rows:    {MergedDrives.Count}");
            foreach (var m in MergedDrives)
                summary.AppendLine($"    [{m.Type}] {m.Name}  used={m.UsedGB}  total={m.TotalGB}  fill={m.FillPercentText}  free={m.FreePercentText}");
            if (StatusData.BadBlockDrives.Any())
                summary.AppendLine($"  Bad block drives:     {string.Join(", ", StatusData.BadBlockDrives)}");
            summary.AppendLine("----------------------");
            AppendConsole(summary.ToString());
            _loggingService.WriteLog("status_summary", summary.ToString());
        }
        }
        catch (Exception ex)
        {
            var errorMsg = $"[ERROR] Status failed:\n{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}";
            AppendConsole(errorMsg);
            _loggingService.WriteLog("status_error", errorMsg);
        }

        IsRunningOperation = false;
        OperationStatus = "Ready";
    }

    private void BuildMergedDrives(StatusData status)
    {
        var diag = new System.Text.StringBuilder();
        diag.AppendLine("=== BuildMergedDrives ===");
        diag.AppendLine($"  ConfigDrives count: {status.ConfigDrives.Count}");
        diag.AppendLine($"  Status Drives count: {status.Drives.Count}");

        var merged = new System.Collections.ObjectModel.ObservableCollection<MergedDriveEntry>();

        // Start from conf entries as the authoritative source
        foreach (var conf in status.ConfigDrives)
        {
            var entry = new MergedDriveEntry
            {
                Name = conf.Name,
                Type = conf.Type,
                Path = conf.Path
            };

            // Enrich with live usage data from status output (data drives only)
            var live = status.Drives.FirstOrDefault(d =>
                string.Equals(d.Name, conf.Name, StringComparison.OrdinalIgnoreCase));
            if (live != null)
            {
                entry.UsedSizeBytes = live.UsedSizeBytes;
                entry.TotalSizeBytes = live.TotalSizeBytes;
                diag.AppendLine($"  MERGED [{conf.Type}] {conf.Name} -> UsedGB={live.UsedSizeBytes / 1073741824.0:F1}, TotalGB={live.TotalSizeBytes / 1073741824.0:F1}");
            }
            else
            {
                diag.AppendLine($"  CONF-ONLY [{conf.Type}] {conf.Name} -> no live usage data (parity/extra)");
            }

            merged.Add(entry);
        }

        // Add any status drives not present in conf (safety net)
        foreach (var live in status.Drives)
        {
            if (!merged.Any(m => string.Equals(m.Name, live.Name, StringComparison.OrdinalIgnoreCase)))
            {
                diag.AppendLine($"  STATUS-ONLY (not in conf) [{live.Type}] {live.Name}");
                merged.Add(new MergedDriveEntry
                {
                    Name = live.Name,
                    Type = live.Type,
                    Path = "—",
                    UsedSizeBytes = live.UsedSizeBytes,
                    TotalSizeBytes = live.TotalSizeBytes
                });
            }
        }

        diag.AppendLine($"  FINAL MergedDrives count: {merged.Count}");
        _loggingService.WriteLog("status_merge_diag", diag.ToString());

        MergedDrives = merged;
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

    /// <summary>Called on window load — runs the full status pipeline including conf parse and drive merge.</summary>
    public void RefreshDashboard() => _ = OnStatus();
}
