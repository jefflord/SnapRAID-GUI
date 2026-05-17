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
///   Everything OK                 → no errors at all
///   N soft errors                 → read errors that parity can correct
///   N unrecoverable errors        → truly lost blocks
/// </summary>
public static class CheckResultParser
{
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
            // ── Summary counters (must be before the flag checks so we read
            //    "0 unrecoverable errors" as zero, not as "has unrecoverable") ──
            if (TryParseCounter(line, "soft errors",          out var se))  result.SoftErrors          = se;
            if (TryParseCounter(line, "io errors",            out var ie))  result.IoErrors            = ie;
            if (TryParseCounter(line, "data errors",          out var de))  result.DataErrors          = de;
            if (TryParseCounter(line, "unrecoverable errors", out var ue))  result.UnrecoverableErrors = ue;
            if (TryParseCounter(line, "recovered errors",     out var re))  result.RecoveredErrors     = re;

            // ── Per-file verdict lines emitted by snapraid ──────────────────
            // "recoverable \"path\""   → bad but fixable
            // "unrecoverable \"path\"" → bad and lost
            // "recovered \"path\""     → was just fixed by snapraid fix
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("recoverable \"",   StringComparison.OrdinalIgnoreCase))
                result.IsRecoverable = true;
            if (trimmed.StartsWith("unrecoverable \"", StringComparison.OrdinalIgnoreCase))
                result.IsUnrecoverableFile = true;
            if (trimmed.StartsWith("recovered \"",     StringComparison.OrdinalIgnoreCase))
                result.WasRecovered = true;

            // ── Other markers ───────────────────────────────────────────────
            if (trimmed.StartsWith("Missing file",    StringComparison.OrdinalIgnoreCase))
                result.IsMissing = true;
            if (trimmed.StartsWith("Everything OK",   StringComparison.OrdinalIgnoreCase))
                result.IsEverythingOk = true;
        }

        // ── Determine final status ───────────────────────────────────────────
        // Priority: Recovered > Unrecoverable > Recoverable/Bad > OK > Unknown
        if (result.WasRecovered)
            result.Status = FileStatus.OK;
        else if (result.IsUnrecoverableFile || result.UnrecoverableErrors > 0)
            result.Status = FileStatus.Unrecoverable;
        else if (result.IsRecoverable || result.IsMissing ||
                 result.SoftErrors > 0 || result.DataErrors > 0 || result.IoErrors > 0)
            result.Status = FileStatus.Bad;
        else if (result.IsEverythingOk)
            result.Status = FileStatus.OK;
        else
            result.Status = FileStatus.Unknown;

        return result;
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

    // Per-file verdict lines
    public bool IsRecoverable      { get; set; }   // "recoverable \"path\""
    public bool IsUnrecoverableFile { get; set; }  // "unrecoverable \"path\""
    public bool WasRecovered       { get; set; }   // "recovered \"path\""
    public bool IsMissing          { get; set; }
    public bool IsEverythingOk     { get; set; }

    // Summary counters
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

