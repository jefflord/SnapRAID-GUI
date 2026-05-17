namespace SnapRAIDGUI.Models;

/// <summary>
/// A unified view of a single drive entry — merging snapraid.conf metadata,
/// live usage from "snapraid status", OS free space, and SMART health data.
/// </summary>
public class MergedDriveEntry
{
    // ── Identity ──────────────────────────────────────────────────────────
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "data";
    /// <summary>Mount path from snapraid.conf (e.g. P:\snapraid.parity or \\?\Volume{...}\)</summary>
    public string Path { get; set; } = string.Empty;

    // ── Usage (from snapraid status — data drives only) ───────────────────
    public long UsedSizeBytes { get; set; }
    public long TotalSizeBytes { get; set; }
    public long FreeSizeBytes => TotalSizeBytes > 0 ? TotalSizeBytes - UsedSizeBytes : OsFreeSizeBytes;
    public double FillPercent => TotalSizeBytes > 0 ? (double)UsedSizeBytes / TotalSizeBytes * 100.0 : 0;
    public double FreePercent => TotalSizeBytes > 0 ? (double)(TotalSizeBytes - UsedSizeBytes) / TotalSizeBytes * 100.0 : 0;
    public bool HasUsageData => TotalSizeBytes > 0;

    // ── OS-level free space (for parity/extra drives via drive letter) ────
    public long OsFreeSizeBytes { get; set; }
    public long OsTotalSizeBytes { get; set; }
    public bool HasOsSpaceData => OsTotalSizeBytes > 0;

    // ── SMART data (from snapraid smart) ──────────────────────────────────
    public int? SmartTemp { get; set; }
    public int? SmartPowerOnDays { get; set; }
    public int? SmartErrorCount { get; set; }
    /// <summary>Failure probability % in next year ("FP" column). Null if not reported.</summary>
    public int? SmartFailPercent { get; set; }
    /// <summary>SSD wear level % if applicable. Null if HDD or not reported.</summary>
    public int? SmartWearLevel { get; set; }
    public bool SmartIsSsd { get; set; }
    public string SmartSerial { get; set; } = string.Empty;
    public double SmartSizeTB { get; set; }
    public bool HasSmartData => SmartTemp.HasValue || SmartPowerOnDays.HasValue;

    // ── Computed display strings ──────────────────────────────────────────
    public string UsedGB => HasUsageData ? $"{UsedSizeBytes / 1073741824.0:F1} GB" : "—";
    public string FreeGB
    {
        get
        {
            if (HasUsageData) return $"{(TotalSizeBytes - UsedSizeBytes) / 1073741824.0:F1} GB";
            if (HasOsSpaceData) return $"{OsFreeSizeBytes / 1073741824.0:F1} GB";
            return "—";
        }
    }
    public string TotalGB
    {
        get
        {
            if (HasUsageData) return $"{TotalSizeBytes / 1073741824.0:F1} GB";
            if (HasOsSpaceData) return $"{OsTotalSizeBytes / 1073741824.0:F1} GB";
            return "—";
        }
    }
    public string FillPercentText => HasUsageData ? $"{FillPercent:F1}%" : "—";
    public string FreePercentText
    {
        get
        {
            if (HasUsageData) return $"{FreePercent:F1}% free";
            if (HasOsSpaceData && OsTotalSizeBytes > 0)
                return $"{(double)OsFreeSizeBytes / OsTotalSizeBytes * 100:F1}% free";
            return "—";
        }
    }
    public double DisplayFillPercent
    {
        get
        {
            if (HasUsageData) return FillPercent;
            if (HasOsSpaceData && OsTotalSizeBytes > 0)
                return 100.0 - (double)OsFreeSizeBytes / OsTotalSizeBytes * 100.0;
            return 0;
        }
    }
    public bool HasAnySpaceData => HasUsageData || HasOsSpaceData;

    public string SmartTempText => SmartTemp.HasValue ? $"{SmartTemp}°C" : "—";
    public string SmartPowerOnText => SmartPowerOnDays.HasValue ? $"{SmartPowerOnDays:N0}d" : "—";
    public string SmartErrorText => SmartErrorCount.HasValue ? SmartErrorCount.ToString()! : "—";
    public string SmartFailText => SmartFailPercent.HasValue ? $"{SmartFailPercent}%" : "—";
    public string SmartWearText
    {
        get
        {
            if (SmartIsSsd) return SmartWearLevel.HasValue ? $"{SmartWearLevel}%" : "SSD";
            return SmartWearLevel.HasValue ? $"{SmartWearLevel}%" : "—";
        }
    }
    public string SmartSizeText => SmartSizeTB > 0 ? $"{SmartSizeTB:F1} TB" : "—";

    /// <summary>
    /// Attempt to populate OS-level free/total space by resolving the drive letter from Path.
    /// Works for simple paths like "P:\..." and also extracts drive letter from Volume GUIDs
    /// if the volume is mounted at a drive letter.
    /// </summary>
    public void PopulateOsFreeSpace()
    {
        try
        {
            // Simple drive letter path e.g. P:\snapraid.parity or C:\
            string? root = null;

            if (Path.Length >= 2 && Path[1] == ':')
            {
                root = Path.Substring(0, 3); // e.g. "P:\"
            }
            else if (Path.StartsWith(@"\\?\Volume", StringComparison.OrdinalIgnoreCase))
            {
                // Try to find which drive letter this volume GUID maps to
                foreach (var drive in System.IO.DriveInfo.GetDrives())
                {
                    try
                    {
                        // GetVolumeNameForVolumeMountPoint returns the GUID path
                        var mountPoint = drive.RootDirectory.FullName;
                        var guidPath = GetVolumeGuidPath(mountPoint);
                        if (guidPath != null && Path.StartsWith(guidPath, StringComparison.OrdinalIgnoreCase))
                        {
                            root = mountPoint;
                            break;
                        }
                    }
                    catch { /* skip unmounted or inaccessible drives */ }
                }
            }

            if (root == null) return;

            var info = new System.IO.DriveInfo(root);
            if (info.IsReady)
            {
                OsFreeSizeBytes = info.AvailableFreeSpace;
                OsTotalSizeBytes = info.TotalSize;
            }
        }
        catch { /* non-critical — leave OS space data as 0 */ }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
    private static extern bool GetVolumeNameForVolumeMountPoint(
        string lpszVolumeMountPoint,
        System.Text.StringBuilder lpszVolumeName,
        uint cchBufferLength);

    private static string? GetVolumeGuidPath(string mountPoint)
    {
        try
        {
            var sb = new System.Text.StringBuilder(50);
            if (GetVolumeNameForVolumeMountPoint(mountPoint, sb, (uint)sb.Capacity))
                return sb.ToString();
        }
        catch { }
        return null;
    }
}
