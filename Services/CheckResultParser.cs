namespace SnapRAIDGUI.Services;

using System.Text.RegularExpressions;
using SnapRAIDGUI.Models;

/// <summary>
/// Parses the output of "snapraid check -f" or "snapraid fix -f".
///
/// Key lines in real snapraid output:
///   recoverable "path"            → file has errors but CAN be fixed
///   unrecoverable "path"          → file errors that CANNOT be fixed
///   recovered "path"              → file was successfully fixed by snapraid fix
///   Missing file '/full/path'     → file tracked by parity but missing on disk
///   Everything OK                 → no errors at all
///   N soft errors / N unrecoverable errors  → summary counters
/// </summary>
public static class CheckResultParser
{
    // Matches: recoverable "rel/path"  |  unrecoverable "rel/path"  |  recovered "rel/path"
    private static readonly Regex VerdictRegex = new(
        @"^(recoverable|unrecoverable|recovered)\s+""(.+)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Matches: Missing file '/full/vol/path/rel/path'
    // We extract the relative portion after the volume GUID path heuristically:
    // the relative path starts after the last PoolPart.*/  segment, or we fall back
    // to the quoted content inside the outer single-quotes.
    private static readonly Regex MissingFileRegex = new(
        @"Missing file '(.+)'",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Parse a single-file check/fix run. Returns one CheckResult for <paramref name="relativePath"/>.
    /// </summary>
    public static CheckResult Parse(string output, string relativePath)
    {
        var result = new CheckResult { RelativePath = relativePath, RawOutput = output };

        if (string.IsNullOrWhiteSpace(output))
        {
            result.Status = FileStatus.Unknown;
            return result;
        }

        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            if (TryParseCounter(line, "soft errors",          out var se))  result.SoftErrors          = se;
            if (TryParseCounter(line, "io errors",            out var ie))  result.IoErrors            = ie;
            if (TryParseCounter(line, "data errors",          out var de))  result.DataErrors          = de;
            if (TryParseCounter(line, "unrecoverable errors", out var ue))  result.UnrecoverableErrors = ue;
            if (TryParseCounter(line, "recovered errors",     out var re))  result.RecoveredErrors     = re;

            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("recoverable \"",   StringComparison.OrdinalIgnoreCase)) result.IsRecoverable       = true;
            if (trimmed.StartsWith("unrecoverable \"", StringComparison.OrdinalIgnoreCase)) result.IsUnrecoverableFile = true;
            if (trimmed.StartsWith("recovered \"",     StringComparison.OrdinalIgnoreCase)) result.WasRecovered        = true;
            if (trimmed.StartsWith("Missing file",     StringComparison.OrdinalIgnoreCase)) result.IsMissing           = true;
            if (trimmed.StartsWith("Everything OK",    StringComparison.OrdinalIgnoreCase)) result.IsEverythingOk      = true;
        }

        result.Status = DetermineStatus(result);
        return result;
    }

    /// <summary>
    /// Parse a folder wildcard check/fix run (e.g. check -f "/folder/*").
    /// Returns a dictionary of relative path → CheckResult for every file mentioned.
    /// Also returns the aggregate counters in <paramref name="totals"/>.
    /// </summary>
    public static Dictionary<string, CheckResult> ParseMulti(string output, out CheckResult totals)
    {
        totals = new CheckResult { RelativePath = "*", RawOutput = output };
        var results = new Dictionary<string, CheckResult>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(output)) return results;

        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            // ── Aggregate counters ──────────────────────────────────────────
            if (TryParseCounter(line, "soft errors",          out var se))  totals.SoftErrors          = se;
            if (TryParseCounter(line, "io errors",            out var ie))  totals.IoErrors            = ie;
            if (TryParseCounter(line, "data errors",          out var de))  totals.DataErrors          = de;
            if (TryParseCounter(line, "unrecoverable errors", out var ue))  totals.UnrecoverableErrors = ue;
            if (TryParseCounter(line, "recovered errors",     out var re))  totals.RecoveredErrors     = re;

            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("Everything OK", StringComparison.OrdinalIgnoreCase))
                totals.IsEverythingOk = true;

            // ── Per-file verdict lines ──────────────────────────────────────
            var vm = VerdictRegex.Match(trimmed);
            if (vm.Success)
            {
                var verdict      = vm.Groups[1].Value.ToLowerInvariant();
                var relativePath = vm.Groups[2].Value; // e.g. "Z-BACKUP/path/file.jpg"

                var r = GetOrCreate(results, relativePath);
                switch (verdict)
                {
                    case "recoverable":   r.IsRecoverable       = true; break;
                    case "unrecoverable": r.IsUnrecoverableFile = true; break;
                    case "recovered":     r.WasRecovered        = true; break;
                }
                continue;
            }

            // ── Missing file lines ──────────────────────────────────────────
            // Missing file '//?/Volume{...}/PoolPart.xxx/rel/path/file'
            // We need to extract just the relative path (after PoolPart.*/)
            var mm = MissingFileRegex.Match(trimmed);
            if (mm.Success)
            {
                var fullPath     = mm.Groups[1].Value;
                var relativePath = ExtractRelativePath(fullPath);
                if (!string.IsNullOrEmpty(relativePath))
                {
                    var r = GetOrCreate(results, relativePath);
                    r.IsMissing = true;
                }
            }
        }

        // Finalise status for each file
        foreach (var r in results.Values)
            r.Status = DetermineStatus(r);

        // If nothing was mentioned but Everything OK — mark totals OK
        if (results.Count == 0 && totals.IsEverythingOk)
            totals.Status = FileStatus.OK;

        return results;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static FileStatus DetermineStatus(CheckResult r)
    {
        if (r.WasRecovered)                                                           return FileStatus.OK;
        if (r.IsUnrecoverableFile || r.UnrecoverableErrors > 0)                      return FileStatus.Unrecoverable;
        if (r.IsRecoverable || r.IsMissing ||
            r.SoftErrors > 0 || r.DataErrors > 0 || r.IoErrors > 0)                 return FileStatus.Bad;
        if (r.IsEverythingOk)                                                         return FileStatus.OK;
        return FileStatus.Unknown;
    }

    private static CheckResult GetOrCreate(Dictionary<string, CheckResult> d, string path)
    {
        if (!d.TryGetValue(path, out var r))
        {
            r = new CheckResult { RelativePath = path };
            d[path] = r;
        }
        return r;
    }

    /// <summary>
    /// Extract the SnapRAID relative path from a full volume path like
    /// //?/Volume{guid}/PoolPart.xxx/Z-BACKUP/file.jpg  →  Z-BACKUP/file.jpg
    /// Falls back to returning the input unchanged if the pattern isn't found.
    /// </summary>
    private static readonly Regex PoolPartRegex = new(
        @"PoolPart\.[^/\\]+[/\\](.*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string ExtractRelativePath(string fullPath)
    {
        var m = PoolPartRegex.Match(fullPath);
        if (m.Success)
            return m.Groups[1].Value.Replace('\\', '/');

        // Fallback: just return whatever is after the last volume segment
        return fullPath.Replace('\\', '/');
    }

    private static bool TryParseCounter(string line, string label, out int value)
    {
        var m = Regex.Match(line, $@"^\s*(\d+)\s+{Regex.Escape(label)}", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out value)) return true;
        value = 0;
        return false;
    }
}

public class CheckResult
{
    public string     RelativePath { get; set; } = string.Empty;
    public string     RawOutput    { get; set; } = string.Empty;
    public FileStatus Status       { get; set; } = FileStatus.Unknown;

    public bool IsRecoverable       { get; set; }
    public bool IsUnrecoverableFile { get; set; }
    public bool WasRecovered        { get; set; }
    public bool IsMissing           { get; set; }
    public bool IsEverythingOk      { get; set; }

    public int SoftErrors          { get; set; }
    public int IoErrors            { get; set; }
    public int DataErrors          { get; set; }
    public int UnrecoverableErrors { get; set; }
    public int RecoveredErrors     { get; set; }

    public string Summary => Status switch
    {
        FileStatus.OK when WasRecovered       => $"Recovered ({RecoveredErrors} blocks fixed)",
        FileStatus.OK                         => "OK",
        FileStatus.Unrecoverable              => $"Unrecoverable ({UnrecoverableErrors} errors)",
        FileStatus.Bad when IsRecoverable     => $"Recoverable — {SoftErrors} soft error(s); use Fix to repair",
        FileStatus.Bad when IsMissing         => "Missing from disk",
        FileStatus.Bad                        => $"Bad ({DataErrors} data, {SoftErrors} soft, {IoErrors} IO errors)",
        _                                     => "Unknown"
    };
}


