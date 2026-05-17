namespace SnapRAIDGUI.Models;

public class StatusData
{
    public bool IsSynced { get; set; }
    public int ParityFragmentationPercent { get; set; }
    public string ArrayAgeStatus { get; set; } = "Unknown";
    public int DaysSinceLastSync { get; set; }
    public List<DriveInfo> Drives { get; set; } = new();
    public List<DriveConfigEntry> ConfigDrives { get; set; } = new();
    public int TotalBlocks { get; set; }
    public int ScrubbedBlocks { get; set; }
    public string ScrubStatus { get; set; } = "N/A";
    public List<string> BadBlockDrives { get; set; } = new();
    public string RawOutput { get; set; } = string.Empty;

    /// <summary>Diagnostic log produced by StatusParser — written to log file for debugging.</summary>
    public string ParseDiagnostics { get; set; } = string.Empty;

    public bool IsClean => ArrayAgeStatus == "Clean" && BadBlockDrives.Count == 0;
}

public class DriveInfo
{
    public string Name { get; set; } = string.Empty;
    public long TotalSizeBytes { get; set; }
    public long UsedSizeBytes { get; set; }
    public double FillPercent => TotalSizeBytes > 0 ? (double)UsedSizeBytes / TotalSizeBytes * 100 : 0;
    public string Type { get; set; } = "data";
}

public class DriveConfigEntry
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Type { get; set; } = "data";
    public int Index { get; set; }
}
