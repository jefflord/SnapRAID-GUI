namespace SnapRAIDGUI.Services;

using System.IO;
using System.Text.RegularExpressions;
using SnapRAIDGUI.Models;

public static class ConfigParser
{
    public static List<DriveConfigEntry> Parse(string confPath)
    {
        var entries = new List<DriveConfigEntry>();

        if (string.IsNullOrWhiteSpace(confPath) || !File.Exists(confPath))
            return entries;

        try
        {
            foreach (var rawLine in File.ReadAllLines(confPath))
            {
                var line = rawLine.Trim();

                // Skip empty lines and full-line comments
                if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                    continue;

                // parity <path>
                var m = Regex.Match(line, @"^parity\s+(\S+)", RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    entries.Add(new DriveConfigEntry { Name = "parity", Path = m.Groups[1].Value, Type = "parity" });
                    continue;
                }

                // 2-parity, 3-parity, ... <path>
                m = Regex.Match(line, @"^(\d+-parity)\s+(\S+)", RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    entries.Add(new DriveConfigEntry { Name = m.Groups[1].Value, Path = m.Groups[2].Value, Type = "parity" });
                    continue;
                }

                // data <name> <path>
                m = Regex.Match(line, @"^data\s+(\S+)\s+(\S+)", RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    entries.Add(new DriveConfigEntry { Name = m.Groups[1].Value, Path = m.Groups[2].Value, Type = "data" });
                    continue;
                }

                // extra <name> <path>
                m = Regex.Match(line, @"^extra\s+(\S+)\s+(\S+)", RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    entries.Add(new DriveConfigEntry { Name = m.Groups[1].Value, Path = m.Groups[2].Value, Type = "extra" });
                    continue;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ConfigParser error: {ex.Message}");
        }

        // Assign display indexes per type
        int parityIdx = 1, dataIdx = 1, extraIdx = 1;
        foreach (var e in entries)
        {
            e.Index = e.Type switch
            {
                "parity" => parityIdx++,
                "data"   => dataIdx++,
                _        => extraIdx++
            };
        }

        return entries;
    }
}
