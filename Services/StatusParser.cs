namespace SnapRAIDGUI.Services;

using System.Text;
using System.Text.RegularExpressions;
using SnapRAIDGUI.Models;

public static class StatusParser
{
    public static StatusData Parse(string output)
    {
        var diag = new StringBuilder();
        diag.AppendLine("=== StatusParser.Parse() ===");

        if (string.IsNullOrWhiteSpace(output))
        {
            diag.AppendLine("INPUT: null or empty — returning empty StatusData");
            return new StatusData { ParseDiagnostics = diag.ToString() };
        }

        diag.AppendLine($"INPUT: {output.Length} chars, {output.Split('\n').Length} lines");

        var data = new StatusData { RawOutput = output };
        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            ParseFragmentation(line, data);
            ParseLastSync(line, data);
            ParseScrubStatus(line, data);
            ParseBadBlocks(line, data);
            ParseDriveInfo(line, data, diag);
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

        diag.AppendLine($"RESULT: ParityFrag={data.ParityFragmentationPercent}%, ArrayStatus={data.ArrayAgeStatus}, DaysSinceSync={data.DaysSinceLastSync}, ScrubStatus={data.ScrubStatus}");
        diag.AppendLine($"RESULT: Drives parsed from status output: {data.Drives.Count}");
        foreach (var d in data.Drives)
            diag.AppendLine($"  DRIVE: Name={d.Name}, Type={d.Type}, Used={d.UsedSizeBytes / 1073741824.0:F1}GB, Total={d.TotalSizeBytes / 1073741824.0:F1}GB, Fill={d.FillPercent:F1}%");

        if (data.Drives.Count == 0)
            diag.AppendLine("  WARNING: No drives parsed! Check ParseDriveInfo regex against raw output.");

        data.ParseDiagnostics = diag.ToString();
        return data;
    }

    private static void ParseFragmentation(string line, StatusData data)
    {
        if (line.IndexOf("parity fragmentation", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            var match = Regex.Match(line, @"(\d+)%?", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var pct))
                data.ParityFragmentationPercent = pct;
        }
    }

    private static void ParseLastSync(string line, StatusData data)
    {
        if (line.IndexOf("days ago", StringComparison.OrdinalIgnoreCase) >= 0 &&
            line.IndexOf("last scrub/sync", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            var match = Regex.Match(line, @"(\d+)\s+days?\s+ago");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var days))
                data.DaysSinceLastSync = days;
        }

        if (line.IndexOf("last sync", StringComparison.OrdinalIgnoreCase) >= 0 &&
            line.IndexOf("fragmentation", StringComparison.OrdinalIgnoreCase) < 0 &&
            line.IndexOf("days ago", StringComparison.OrdinalIgnoreCase) < 0)
        {
            var colonIdx = line.IndexOf(':');
            if (colonIdx > 0 && colonIdx + 1 < line.Length)
            {
                var dateStr = line.Substring(colonIdx + 1).Trim();
                if (DateTime.TryParse(dateStr, out var syncDate))
                    data.DaysSinceLastSync = (int)(DateTime.Now - syncDate).TotalDays;
            }

            if (line.IndexOf("never", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("not yet", StringComparison.OrdinalIgnoreCase) >= 0)
                data.DaysSinceLastSync = -1;
        }
    }

    private static void ParseScrubStatus(string line, StatusData data)
    {
        var notScrubbedMatch = Regex.Match(line,
            @"(\d+)%\s+of\s+the\s+array\s+is\s+(not\s+)?scrubbed", RegexOptions.IgnoreCase);
        if (notScrubbedMatch.Success && int.TryParse(notScrubbedMatch.Groups[1].Value, out var scrubPct))
        {
            bool isNot = notScrubbedMatch.Groups[2].Success;
            data.ScrubStatus = isNot ? $"{scrubPct}% unscrubbed" : $"Scrubbed {scrubPct}%";
        }

        if (line.IndexOf("no error detected", StringComparison.OrdinalIgnoreCase) >= 0 &&
            string.IsNullOrEmpty(data.ArrayAgeStatus))
            data.ArrayAgeStatus = "Clean";
    }

    private static void ParseBadBlocks(string line, StatusData data)
    {
        if (line.IndexOf("bad block", StringComparison.OrdinalIgnoreCase) >= 0 ||
            line.IndexOf("silent error", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            var match = Regex.Match(line, @"(d\d+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var driveName = match.Groups[1].Value;
                if (!data.BadBlockDrives.Contains(driveName))
                    data.BadBlockDrives.Add(driveName);
            }
        }
    }

    private static void ParseDriveInfo(string line, StatusData data, StringBuilder diag)
    {
        // Skip known header/separator lines
        if (line.Contains("------") ||
            line.Contains("status report", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("Files", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("Fragmented", StringComparison.OrdinalIgnoreCase))
            return;

        // Real snapraid status drive line format:
        //   310390       0       0    30.5    6119    5825  51% DrivePool_1
        // Columns: files  fragmented  excess  wasted_gb  used_gb  free_gb  use%  name
        //
        // Pattern: starts with whitespace+digits, contains integers/decimals,
        // then an integer%, then the drive name at the end.
        var match = Regex.Match(line, @"^\s*\d[\d\s.]+\s+(\d+)%\s+(\S+)\s*$");

        if (!match.Success)
            return;

        if (!int.TryParse(match.Groups[1].Value, out var usePercent))
            return;

        var driveName = match.Groups[2].Value.Trim();
        if (string.IsNullOrEmpty(driveName))
            return;

        // Extract all numeric tokens before the "XX%" to get used_gb and free_gb
        var beforePct = line.Substring(0, match.Groups[1].Index).Trim();
        var numTokens = Regex.Matches(beforePct, @"-?\d+(?:\.\d+)?");

        double usedGB = 0, freeGB = 0;
        if (numTokens.Count >= 2)
        {
            double.TryParse(numTokens[numTokens.Count - 2].Value,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out usedGB);
            double.TryParse(numTokens[numTokens.Count - 1].Value,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out freeGB);
        }

        var totalGB = usedGB + freeGB;

        diag.AppendLine($"DRIVE LINE MATCHED: '{line.Trim()}'");
        diag.AppendLine($"  -> Name={driveName}, Use%={usePercent}, UsedGB={usedGB}, FreeGB={freeGB}, TotalGB={totalGB}");

        var drive = new DriveInfo
        {
            Name = driveName,
            Type = "data",
            UsedSizeBytes = (long)(usedGB * 1024L * 1024L * 1024L),
            TotalSizeBytes = (long)(totalGB * 1024L * 1024L * 1024L)
        };

        if (!data.Drives.Any(d => d.Name == drive.Name))
            data.Drives.Add(drive);
        else
            diag.AppendLine($"  -> SKIPPED (duplicate)");
    }
}
