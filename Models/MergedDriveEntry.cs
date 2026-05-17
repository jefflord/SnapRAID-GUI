namespace SnapRAIDGUI.Models;

/// <summary>
/// A unified view of a single drive entry — merging snapraid.conf metadata
/// with live usage data from "snapraid status" output.
/// </summary>
public class MergedDriveEntry
{
    /// <summary>Drive name (e.g. DrivePool_1, parity, boot)</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Type: parity, data, extra</summary>
    public string Type { get; set; } = "data";

    /// <summary>Mount path from snapraid.conf</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Used size in bytes (from status output, 0 if not available)</summary>
    public long UsedSizeBytes { get; set; }

    /// <summary>Total size in bytes (from status output, 0 if not available)</summary>
    public long TotalSizeBytes { get; set; }

    /// <summary>Free size in bytes</summary>
    public long FreeSizeBytes => TotalSizeBytes - UsedSizeBytes;

    /// <summary>Fill percent 0–100. 0 if no size data.</summary>
    public double FillPercent => TotalSizeBytes > 0 ? (double)UsedSizeBytes / TotalSizeBytes * 100.0 : 0;

    /// <summary>Free percent 0–100.</summary>
    public double FreePercent => TotalSizeBytes > 0 ? (double)FreeSizeBytes / TotalSizeBytes * 100.0 : 0;

    /// <summary>True if live usage data was populated from status output.</summary>
    public bool HasUsageData => TotalSizeBytes > 0;

    /// <summary>Display string for used GB.</summary>
    public string UsedGB => HasUsageData ? $"{UsedSizeBytes / 1073741824.0:F1} GB" : "—";

    /// <summary>Display string for free GB.</summary>
    public string FreeGB => HasUsageData ? $"{FreeSizeBytes / 1073741824.0:F1} GB" : "—";

    /// <summary>Display string for total GB.</summary>
    public string TotalGB => HasUsageData ? $"{TotalSizeBytes / 1073741824.0:F1} GB" : "—";

    /// <summary>Display string for fill percent.</summary>
    public string FillPercentText => HasUsageData ? $"{FillPercent:F1}%" : "—";

    /// <summary>Display string for free percent.</summary>
    public string FreePercentText => HasUsageData ? $"{FreePercent:F1}% free" : "—";
}
