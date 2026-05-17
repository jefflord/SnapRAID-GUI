namespace SnapRAIDGUI.Services;

using System.Text.RegularExpressions;
using SnapRAIDGUI.Models;

/// <summary>
/// Parses the output of "snapraid smart" into a dictionary keyed by drive name.
/// 
/// Example output line:
///      32   1940       -   4%    - 12.0  5PJ85ZUE      /dev/pd0  DrivePool_1
/// Columns: Temp  PowerOnDays  ErrorCount  FP  Wear  SizeTB  Serial  Device  DiskName
/// 
/// FP and Wear can be "-" (not available) or "SSD" (for SSD wear level).
/// </summary>
public static class SmartParser
{
    public static Dictionary<string, SmartEntry> Parse(string output)
    {
        var result = new Dictionary<string, SmartEntry>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(output))
            return result;

        foreach (var rawLine in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();

            // Skip headers and separator lines
            if (line.StartsWith("SnapRAID", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Temp", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("C On", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("---") ||
                line.StartsWith("The ") ||
                line.StartsWith("Probability") ||
                string.IsNullOrWhiteSpace(line))
                continue;

            var entry = ParseLine(line);
            if (entry != null && !string.IsNullOrEmpty(entry.DiskName))
                result[entry.DiskName] = entry;
        }

        return result;
    }

    private static SmartEntry? ParseLine(string line)
    {
        // The line ends with:  /dev/pdX  DiskName
        // Walk from right to get DiskName and Device, then parse the remaining tokens left-to-right.
        //
        // Tokenize on whitespace — the fixed columns are all single tokens.
        var tokens = Regex.Split(line.Trim(), @"\s+");

        // Minimum: Temp PowerOnDays ErrorCount FP Wear SizeTB Serial Device DiskName = 9 tokens
        if (tokens.Length < 9) return null;

        var diskName = tokens[tokens.Length - 1];
        var device   = tokens[tokens.Length - 2];
        var serial   = tokens[tokens.Length - 3];

        if (!double.TryParse(tokens[tokens.Length - 4],
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture,
            out var sizeTB))
            sizeTB = 0;

        // Wear column (index from right = 4): may be "-", "SSD", or "N%"
        var wearToken = tokens[tokens.Length - 5];
        int? wear = null;
        bool isSsd = false;
        if (wearToken.Equals("SSD", StringComparison.OrdinalIgnoreCase))
            isSsd = true;
        else if (wearToken != "-")
        {
            var wearMatch = Regex.Match(wearToken, @"(\d+)");
            if (wearMatch.Success) wear = int.Parse(wearMatch.Groups[1].Value);
        }

        // FP column (index from right = 5): may be "-" or "N%"
        var fpToken = tokens[tokens.Length - 6];
        int? fp = null;
        if (fpToken != "-")
        {
            var fpMatch = Regex.Match(fpToken, @"(\d+)");
            if (fpMatch.Success) fp = int.Parse(fpMatch.Groups[1].Value);
        }

        // ErrorCount (index from right = 7): may be "-" or integer
        var errToken = tokens[tokens.Length - 7];
        int? errors = null;
        if (errToken != "-" && int.TryParse(errToken, out var errVal))
            errors = errVal;

        // PowerOnDays (index from right = 8)
        int? powerOnDays = null;
        if (int.TryParse(tokens[tokens.Length - 8], out var pod))
            powerOnDays = pod;

        // Temp (index from right = 9)
        int? temp = null;
        if (int.TryParse(tokens[tokens.Length - 9], out var tempVal))
            temp = tempVal;

        return new SmartEntry
        {
            DiskName      = diskName,
            Device        = device,
            Serial        = serial,
            SizeTB        = sizeTB,
            WearLevel     = wear,
            IsSsd         = isSsd,
            FailPercent   = fp,
            ErrorCount    = errors,
            PowerOnDays   = powerOnDays,
            Temp          = temp
        };
    }
}

public class SmartEntry
{
    public string DiskName    { get; set; } = string.Empty;
    public string Device      { get; set; } = string.Empty;
    public string Serial      { get; set; } = string.Empty;
    public double SizeTB      { get; set; }
    public int?   Temp        { get; set; }
    public int?   PowerOnDays { get; set; }
    public int?   ErrorCount  { get; set; }
    /// <summary>Failure probability % (FP column). Null = not reported.</summary>
    public int?   FailPercent { get; set; }
    public int?   WearLevel   { get; set; }
    public bool   IsSsd       { get; set; }
}
