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

    public RecoveryViewModel RecoveryViewModel { get; }

    [ObservableProperty] private string _consoleOutput = string.Empty;
    [ObservableProperty] private StatusData? _statusData;
    [ObservableProperty] private DiffData? _diffData;
    [ObservableProperty] private bool _isRunningOperation;
    [ObservableProperty] private string _operationStatus = "Ready";
    [ObservableProperty] private string _logDirectoryPath = string.Empty;
    [ObservableProperty] private System.Collections.ObjectModel.ObservableCollection<MergedDriveEntry> _mergedDrives = new();

    // Per-operation output panels
    [ObservableProperty] private string _diffOutput   = string.Empty;
    [ObservableProperty] private string _syncOutput   = string.Empty;
    [ObservableProperty] private string _scrubOutput  = string.Empty;
    [ObservableProperty] private string _smartOutput  = string.Empty;
    [ObservableProperty] private string _fixOutput    = string.Empty;
    [ObservableProperty] private string _statusOutput = string.Empty;

    // Which operation-specific output property is currently active
    private Action<string>? _activeOutputAppender;

    public RelayCommand SaveSettingsCommand { get; }
    public RelayCommand BrowseSnapRaidExeCommand { get; }
    public RelayCommand BrowseConfFileCommand { get; }

 private void Status() => _ = OnStatus();
    [RelayCommand(CanExecute = nameof(CanRunOperation))] private void Diff() => _ = OnDiff();
    [RelayCommand(CanExecute = nameof(CanRunOperation))] private void Sync() => _ = OnSync();
    [RelayCommand(CanExecute = nameof(CanRunOperation))] private void ScrubNew() => _ = OnScrub(false);
    [RelayCommand(CanExecute = nameof(CanRunOperation))] private void ScrubFull() => _ = OnScrub(true);
    [RelayCommand(CanExecute = nameof(CanRunOperation))] private void Fix() => OnFix();
    [RelayCommand(CanExecute = nameof(CanRunOperation))] private void Smart() => RunAsync(async () =>
    {
        SmartOutput = string.Empty;
        _activeOutputAppender = t => SmartOutput += t;
        await _snapRAIDService.RunSmartAsync();
        _activeOutputAppender = null;
    });
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

        RecoveryViewModel = new RecoveryViewModel(
            _snapRAIDService,
            _loggingService,
            () => Settings?.ConfirmOnFix ?? false);

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

        SyncOutput = string.Empty;
        _activeOutputAppender = t => SyncOutput += t;
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

            if (!result) { AppendConsole("Sync cancelled by user.\n"); _activeOutputAppender = null; return; }
        }

        ClearConsole();
        AppendConsole("Starting sync...\n");
        IsRunningOperation = true;
        OperationStatus = "Sync in progress...";

        var output = await _snapRAIDService.RunSyncAsync();
        _loggingService.WriteLog("sync", output);

        _activeOutputAppender = null;
        IsRunningOperation = false;
        OperationStatus = "Ready";
    }

    private async Task OnScrub(bool full)
    {
        if (!ValidateSnapRaidPath()) return;

        ScrubOutput = string.Empty;
        _activeOutputAppender = t => ScrubOutput += t;
        var mode = full ? "Full" : "New";
        ClearConsole();
        AppendConsole($"Starting {mode} scrub...\n");
        IsRunningOperation = true;
        OperationStatus = $"Scrub ({mode}) in progress...";

        var output = await _snapRAIDService.RunScrubAsync(full);
        _loggingService.WriteLog("scrub", output);

        _activeOutputAppender = null;
        IsRunningOperation = false;
        OperationStatus = "Ready";
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

        FixOutput = string.Empty;
        _activeOutputAppender = t => FixOutput += t;
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
                _activeOutputAppender = null;
                IsRunningOperation = false;
                OperationStatus = "Ready";
            });
        });
    }

    private async Task OnDiff()
    {
        if (!ValidateSnapRaidPath()) return;

        DiffOutput = string.Empty;
        _activeOutputAppender = t => DiffOutput += t;
        ClearConsole();
        AppendConsole("Running diff...\n");
        IsRunningOperation = true;
        OperationStatus = "Diff in progress...";

        var output = await _snapRAIDService.RunDiffAsync();
        DiffData = DiffParser.Parse(output);
        _loggingService.WriteLog("diff", output);

        var summary = new System.Text.StringBuilder();
        if (DiffData?.RawOutput == null || !DiffData.RawOutput.Contains("added:", StringComparison.OrdinalIgnoreCase))
        {
            summary.AppendLine("[INFO] No changes detected — array is in sync.");
        }
        else
        {
            summary.AppendLine($"Added:   {DiffData.AddedFiles}");
            summary.AppendLine($"Removed: {DiffData.RemovedFiles}");
            summary.AppendLine($"Updated: {DiffData.UpdatedFiles}");
            summary.AppendLine($"Moved:   {DiffData.MovedFiles}");
            summary.AppendLine($"Copied:  {DiffData.CopiedFiles}");
            summary.AppendLine($"Equal:   {DiffData.EqualFiles}");
            if (DiffData.HasLargeDeletions(Settings?.DeletionWarningThreshold ?? 50))
                summary.AppendLine($"[WARNING] Large deletions detected!");
        }
        AppendConsole("\n" + summary);

        _activeOutputAppender = null;
        IsRunningOperation = false;
        OperationStatus = "Ready";
    }

    private async Task OnStatus()
    {
        if (!ValidateSnapRaidPath()) return;

        ClearConsole();
        AppendConsole("Fetching status and SMART data...\n");
        IsRunningOperation = true;
        OperationStatus = "Refreshing...";

        try
        {
            // Run status and smart in parallel
            var statusTask = _snapRAIDService.RunStatusAsync();
            var smartTask  = _snapRAIDService.RunSmartAsync();
            await Task.WhenAll(statusTask, smartTask);

            var statusOutput = statusTask.Result;
            var smartOutput  = smartTask.Result;

            StatusData = StatusParser.Parse(statusOutput);
            var smartData = SmartParser.Parse(smartOutput);

            // Log raw outputs and diagnostics
            _loggingService.WriteLog("status", statusOutput);
            _loggingService.WriteLog("smart",  smartOutput);
            _loggingService.WriteLog("status_parse_diag", StatusData.ParseDiagnostics);

            // Parse snapraid.conf
            if (!string.IsNullOrWhiteSpace(Settings?.ConfFilePath) && File.Exists(Settings.ConfFilePath))
            {
                try { StatusData.ConfigDrives = ConfigParser.Parse(Settings.ConfFilePath); }
                catch (Exception ex) { AppendConsole($"[WARN] Could not parse snapraid.conf: {ex.Message}\n"); }
            }
            else
            {
                AppendConsole($"[WARN] snapraid.conf not found or path empty: '{Settings?.ConfFilePath}'\n");
            }

            BuildMergedDrives(StatusData, smartData);

            if (StatusData != null)
            {
                var summary = new System.Text.StringBuilder();
                summary.AppendLine("\n--- Status Summary ---");
                summary.AppendLine($"  Array status:         {StatusData.ArrayAgeStatus}");
                summary.AppendLine($"  Parity fragmentation: {StatusData.ParityFragmentationPercent}%");
                summary.AppendLine($"  Days since sync:      {StatusData.DaysSinceLastSync}");
                summary.AppendLine($"  Scrub status:         {StatusData.ScrubStatus}");
                summary.AppendLine($"  Drives from status:   {StatusData.Drives.Count}");
                foreach (var d in StatusData.Drives)
                    summary.AppendLine($"    [{d.Type}] {d.Name}  used={d.UsedSizeBytes / 1073741824.0:F1}GB  total={d.TotalSizeBytes / 1073741824.0:F1}GB");
                summary.AppendLine($"  Conf drives:          {StatusData.ConfigDrives.Count}");
                foreach (var c in StatusData.ConfigDrives)
                    summary.AppendLine($"    [{c.Type}] {c.Name}  {c.Path}");
                summary.AppendLine($"  SMART entries:        {smartData.Count}");
                foreach (var s in smartData.Values)
                    summary.AppendLine($"    {s.DiskName}  temp={s.Temp}°C  days={s.PowerOnDays}  err={s.ErrorCount}  fp={s.FailPercent}%");
                summary.AppendLine($"  Merged drive rows:    {MergedDrives.Count}");
                foreach (var m in MergedDrives)
                    summary.AppendLine($"    [{m.Type}] {m.Name}  used={m.UsedGB}  total={m.TotalGB}  fill={m.FillPercentText}  free={m.FreePercentText}  temp={m.SmartTempText}");
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

    private void BuildMergedDrives(StatusData status, Dictionary<string, SmartEntry>? smartData = null)
    {
        var diag = new System.Text.StringBuilder();
        diag.AppendLine("=== BuildMergedDrives ===");
        diag.AppendLine($"  ConfigDrives: {status.ConfigDrives.Count}, StatusDrives: {status.Drives.Count}, SmartEntries: {smartData?.Count ?? 0}");

        var merged = new System.Collections.ObjectModel.ObservableCollection<MergedDriveEntry>();

        foreach (var conf in status.ConfigDrives)
        {
            var entry = new MergedDriveEntry { Name = conf.Name, Type = conf.Type, Path = conf.Path };

            // Merge status usage data (data drives)
            var live = status.Drives.FirstOrDefault(d =>
                string.Equals(d.Name, conf.Name, StringComparison.OrdinalIgnoreCase));
            if (live != null)
            {
                entry.UsedSizeBytes  = live.UsedSizeBytes;
                entry.TotalSizeBytes = live.TotalSizeBytes;
                diag.AppendLine($"  USAGE [{conf.Type}] {conf.Name}: used={live.UsedSizeBytes / 1073741824.0:F1}GB total={live.TotalSizeBytes / 1073741824.0:F1}GB");
            }

            // For parity/extra drives (or any drive without status data), get OS free space
            if (!entry.HasUsageData)
            {
                entry.PopulateOsFreeSpace();
                if (entry.HasOsSpaceData)
                    diag.AppendLine($"  OS-SPACE [{conf.Type}] {conf.Name}: free={entry.OsFreeSizeBytes / 1073741824.0:F1}GB total={entry.OsTotalSizeBytes / 1073741824.0:F1}GB");
                else
                    diag.AppendLine($"  NO-SPACE [{conf.Type}] {conf.Name}: could not resolve drive letter from path '{conf.Path}'");
            }

            // Merge SMART data
            if (smartData != null && smartData.TryGetValue(conf.Name, out var smart))
            {
                entry.SmartTemp        = smart.Temp;
                entry.SmartPowerOnDays = smart.PowerOnDays;
                entry.SmartErrorCount  = smart.ErrorCount;
                entry.SmartFailPercent = smart.FailPercent;
                entry.SmartWearLevel   = smart.WearLevel;
                entry.SmartIsSsd       = smart.IsSsd;
                entry.SmartSerial      = smart.Serial;
                entry.SmartSizeTB      = smart.SizeTB;
                diag.AppendLine($"  SMART [{conf.Type}] {conf.Name}: temp={smart.Temp}°C days={smart.PowerOnDays} err={smart.ErrorCount} fp={smart.FailPercent}%");
            }

            merged.Add(entry);
        }

        // Safety net: status drives not in conf
        foreach (var live in status.Drives)
        {
            if (!merged.Any(m => string.Equals(m.Name, live.Name, StringComparison.OrdinalIgnoreCase)))
            {
                diag.AppendLine($"  STATUS-ONLY (not in conf) {live.Name}");
                var entry = new MergedDriveEntry { Name = live.Name, Type = live.Type, Path = "—", UsedSizeBytes = live.UsedSizeBytes, TotalSizeBytes = live.TotalSizeBytes };
                if (smartData != null && smartData.TryGetValue(live.Name, out var smart))
                {
                    entry.SmartTemp = smart.Temp; entry.SmartPowerOnDays = smart.PowerOnDays;
                    entry.SmartErrorCount = smart.ErrorCount; entry.SmartFailPercent = smart.FailPercent;
                    entry.SmartWearLevel = smart.WearLevel; entry.SmartIsSsd = smart.IsSsd;
                    entry.SmartSerial = smart.Serial; entry.SmartSizeTB = smart.SizeTB;
                }
                merged.Add(entry);
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
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            AppendConsole(output);
            _activeOutputAppender?.Invoke(output);
        });
    }

    private void OnExitCodeReceived(object? sender, int code) { /* update status if needed */ }

    private void OnErrorOccurred(object? sender, string error)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() => AppendConsole($"[ERROR] {error}\n"));
    }

    /// <summary>Called on window load — runs the full status pipeline including conf parse and drive merge.</summary>
    public void RefreshDashboard() => _ = OnStatus();
}
