namespace SnapRAIDGUI.Models;

public class SnapRAIDSettings
{
    public string SnapRaidExePath { get; set; } = "snapraid.exe";
    public string ConfFilePath { get; set; } = string.Empty;
    public int DeletionWarningThreshold { get; set; } = 50;
    public bool AutoRefreshStatus { get; set; } = true;
    public int RefreshIntervalSeconds { get; set; } = 30;
    public string LogDirectory { get; set; } = "logs";
}
