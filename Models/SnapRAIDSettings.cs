namespace SnapRAIDGUI.Models;

using CommunityToolkit.Mvvm.ComponentModel;

public partial class SnapRAIDSettings : ObservableObject
{
    [ObservableProperty] private string _snapRaidExePath = "snapraid.exe";
    [ObservableProperty] private string _confFilePath = string.Empty;
    [ObservableProperty] private int _deletionWarningThreshold = 50;
    [ObservableProperty] private bool _autoRefreshStatus = true;
    [ObservableProperty] private int _refreshIntervalSeconds = 30;
    [ObservableProperty] private string _logDirectory = "logs";
    [ObservableProperty] private bool _confirmOnFix = false;

    public SnapRAIDSettings() { }

    // Copy constructor for creating a fresh copy of current settings
    public SnapRAIDSettings(SnapRAIDSettings other)
    {
        SnapRaidExePath = other.SnapRaidExePath;
        ConfFilePath = other.ConfFilePath;
        DeletionWarningThreshold = other.DeletionWarningThreshold;
        AutoRefreshStatus = other.AutoRefreshStatus;
        RefreshIntervalSeconds = other.RefreshIntervalSeconds;
        LogDirectory = other.LogDirectory;
        ConfirmOnFix = other.ConfirmOnFix;
    }
}
