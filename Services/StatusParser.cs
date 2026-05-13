namespace SnapRAIDGUI.Services;

using SnapRAIDGUI.Models;

public static class StatusParser
{
    public static StatusData Parse(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return new StatusData();

        var data = new StatusData { RawOutput = output };

        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            // Parity fragmentation: "Parity fragmentation: 45%" or similar
            ParseFragmentation(line, data);

            // Last sync date
            ParseLastSync(line, data);

            // Scrub progress/status
            ParseScrubStatus(line, data);

            // Bad blocks / silent errors
            ParseBadBlocks(line, data);

            // Drive info lines (d1, d2, parity, etc.)
            ParseDriveInfo(line, data);
        }

        // Determine array age status as fallback
        if (string.IsNullOrEmpty(data.ArrayAgeStatus))
        {
            if (data.ParityFragmentationPercent == 0 && data.BadBlockDrives.Count == 0)
                data.ArrayAgeStatus = "Clean";
            else if (data.ParityFragmentationPercent > 0 && data.ParityFragmentationPercent < 100)
                data.ArrayAgeStatus = "Sync Required";
            else if (data.DaysSinceLastSync > 7)
                data.ArrayAgeStatus = "Out of Date";
            else
                data.ArrayAgeStatus = "Unknown";
        }

        return data;
    }

    private static void ParseFragmentation(string line, StatusData data)
    {
        if (line.IndexOf("parity fragmentation", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            var match = System.Text.RegularExpressions.Regex.Match(line, @"(\d+)%?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var pct))
                data.ParityFragmentationPercent = pct;
        }
    }

    private static void ParseLastSync(string line, StatusData data)
    {
        if (line.IndexOf("last sync", StringComparison.OrdinalIgnoreCase) >= 0 &&
            line.IndexOf("fragmentation", StringComparison.OrdinalIgnoreCase) < 0)
        {
            // Handle "never" or "not yet synced"
            if (line.IndexOf("never", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("not yet", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                data.DaysSinceLastSync = -1;
                return;
            }

            // Try to extract date after "last sync:" or "last sync"
            var colonIdx = line.IndexOf(':');
            if (colonIdx > 0 && colonIdx + 1 < line.Length)
            {
                var dateStr = line.Substring(colonIdx + 1).Trim();
                // Handle formats like: "2024-01-15 14:30", "Jan 15, 2024", etc.
                if (DateTime.TryParse(dateStr, out var syncDate))
                    data.DaysSinceLastSync = (int)(DateTime.Now - syncDate).TotalDays;
            }
        }
    }

    private static void ParseScrubStatus(string line, StatusData data)
    {
        // Scrub progress: "Scrub progress: 75%" or similar
        if (line.IndexOf("scrub progress", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            var match = System.Text.RegularExpressions.Regex.Match(line, @"(\d+)%?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var pct))
            {
                data.ScrubStatus = $"{pct}%";
                // Try to estimate total blocks from context
                var allMatch = System.Text.RegularExpressions.Regex.Match(line, @"(\d+)\s*/\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (allMatch.Success && int.TryParse(allMatch.Groups[1].Value, out var done) && int.TryParse(allMatch.Groups[2].Value, out var total))
                {
                    data.ScrubbedBlocks = done;
                    data.TotalBlocks = total;
                }
            }
        }

        // Scrub status: "Scrub: not started", "Scrub: in progress", etc.
        if (line.IndexOf("scrub:", StringComparison.OrdinalIgnoreCase) >= 0 &&
            line.IndexOf("progress", StringComparison.OrdinalIgnoreCase) < 0)
        {
            var scrubState = line.Substring(line.IndexOf("scrub:", StringComparison.OrdinalIgnoreCase) + 6).Trim();
            data.ScrubStatus = string.IsNullOrEmpty(scrubState) ? "N/A" : scrubState;
        }
    }

    private static void ParseBadBlocks(string line, StatusData data)
    {
        if ((line.IndexOf("bad block", StringComparison.OrdinalIgnoreCase) >= 0 ||
             line.IndexOf("silent error", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            // Try to extract drive name: "d1: bad blocks" or similar
            var match = System.Text.RegularExpressions.Regex.Match(line, @"(d\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var driveName = match.Groups[1].Value;
                if (!data.BadBlockDrives.Contains(driveName))
                    data.BadBlockDrives.Add(driveName);
            }
        }
    }

    private static void ParseDriveInfo(string line, StatusData data)
    {
        // Match drive lines like: "d1  F:\disk1\   4096 GB  2500 GB" or "parity  E:\parity\   4096 GB  0 GB"
        // Use regex to find the pattern: name + path + two numbers with optional unit
        var match = System.Text.RegularExpressions.Regex.Match(line, @"^(d\d+|parity)\s+(.+?)\s+(\d[\d,.]*)\s*(GB|TB|MB)?\s+(\d[\d,.]*)\s*(GB|TB|MB)?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (!match.Success)
            return;

        var driveName = match.Groups[1].Value;
        var drivePath = match.Groups[2].Value.Trim();
        var totalStr = match.Groups[3].Value.Replace(",", "");
        var unit1 = match.Groups[4].Value.ToUpperInvariant();
        var usedStr = match.Groups[5].Value.Replace(",", "");
        var unit2 = match.Groups[6].Value.ToUpperInvariant();

        // Determine base unit (default to GB if not specified)
        var unit = string.IsNullOrEmpty(unit1) ? "GB" : unit1;
        double totalGB, usedGB;

        switch (unit)
        {
            case "TB": totalGB = double.Parse(totalStr) * 1024; break;
            case "MB": totalGB = double.Parse(totalStr) / 1024; break;
            default: totalGB = double.Parse(totalStr); break;
        }

        var usedUnit = string.IsNullOrEmpty(unit2) ? unit : unit2;
        switch (usedUnit)
        {
            case "TB": usedGB = double.Parse(usedStr) * 1024; break;
            case "MB": usedGB = double.Parse(usedStr) / 1024; break;
            default: usedGB = double.Parse(usedStr); break;
        }

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
