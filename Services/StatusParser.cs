namespace SnapRAIDGUI.Services;

using SnapRAIDGUI.Models;

public static class StatusParser
{
    public static StatusData Parse(string output)
    {
        var data = new StatusData { RawOutput = output };

        foreach (var line in output.Split('\n', '\r'))
        {
            var trimmed = line.Trim();

            // Parity fragmentation: "Parity fragmentation: 45%" or similar
            if (trimmed.StartsWith("Parity fragmentation:", StringComparison.OrdinalIgnoreCase))
            {
                var percentStr = trimmed.Replace("Parity fragmentation:", "").Trim().Replace("%", "");
                if (int.TryParse(percentStr, out var pct))
                    data.ParityFragmentationPercent = pct;
            }

            // Array age / "last sync" info
            if (trimmed.StartsWith("Last sync:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("last sync", StringComparison.OrdinalIgnoreCase))
            {
                ParseLastSync(trimmed, data);
            }

            // Scrub status: "Scrub progress:" or similar
            if (trimmed.StartsWith("Scrub progress:", StringComparison.OrdinalIgnoreCase))
            {
                data.ScrubStatus = trimmed.Replace("Scrub progress:", "").Trim();
            }

            // Bad blocks drive detection
            if (trimmed.Contains("bad block", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("silent error", StringComparison.OrdinalIgnoreCase))
            {
                var driveName = ExtractDriveNameFromLine(trimmed);
                if (!string.IsNullOrEmpty(driveName) && !data.BadBlockDrives.Contains(driveName))
                    data.BadBlockDrives.Add(driveName);
            }

            // Drive info: "d1  F:\disk1\   4096 GB  2500 GB" or similar format
            ParseDriveInfo(trimmed, data);
        }

        // Determine array age status
        if (data.ParityFragmentationPercent == 100 && data.BadBlockDrives.Count == 0)
            data.ArrayAgeStatus = "Clean";
        else if (data.ParityFragmentationPercent > 0 && data.ParityFragmentationPercent < 100)
            data.ArrayAgeStatus = "Sync Required";
        else if (data.DaysSinceLastSync > 7)
            data.ArrayAgeStatus = "Out of Date";

        return data;
    }

    private static void ParseLastSync(string line, StatusData data)
    {
        // Try to extract date from lines like:
        // "Last sync: 2024-01-15 14:30" or similar formats
        var parts = line.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            var dateStr = parts[1].Trim();
            if (DateTime.TryParse(dateStr, out var syncDate))
            {
                data.DaysSinceLastSync = (DateTime.Now - syncDate).Days;
            }
        }
    }

    private static void ParseDriveInfo(string line, StatusData data)
    {
        // Match patterns like: "d1  F:\disk1\   4096 GB  2500 GB" or "parity  E:\parity\   4096 GB  0 GB"
        var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 4)
        {
            // Check if first part looks like a drive name and second looks like a path
            var driveName = parts[0];
            var drivePath = parts[1];

            if ((drivePath.EndsWith("\\") || drivePath.Contains(":\\")) && IsNumericGB(parts[parts.Length - 2]))
            {
                var totalStr = parts[parts.Length - 2].Replace("GB", "").Trim();
                var usedStr = parts[parts.Length - 1].Replace("GB", "").Trim();

                if (double.TryParse(totalStr, out var totalGB) && double.TryParse(usedStr, out var usedGB))
                {
                    var drive = new DriveInfo
                    {
                        Name = driveName,
                        Type = driveName == "parity" ? "parity" : "data",
                        TotalSizeBytes = (long)(totalGB * 1024L * 1024L * 1024L),
                        UsedSizeBytes = (long)(usedGB * 1024L * 1024L * 1024L)
                    };

                    if (!data.Drives.Any(d => d.Name == drive.Name))
                        data.Drives.Add(drive);
                }
            }
        }
    }

    private static bool IsNumericGB(string s)
    {
        var clean = s.Replace("GB", "").Trim();
        return double.TryParse(clean, out _);
    }

    private static string? ExtractDriveNameFromLine(string line)
    {
        // Try to extract drive name from context like "d1: bad blocks" or similar
        var parts = line.Split(new[] { ' ', '\t', ':' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part.StartsWith("d") && int.TryParse(part.Substring(1), out _))
                return part;
        }
        return null;
    }
}
