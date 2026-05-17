namespace SnapRAIDGUI.Services;

using System.Text.RegularExpressions;
using SnapRAIDGUI.Models;

/// <summary>
/// Parses the output of "snapraid list".
///
/// Each line looks like:
///      2458358 2021/12/11 09:19 "Digital Photography/2009/08/DSC_9509.JPG"
/// Format: SIZE  DATE  TIME  "RELATIVE/PATH"
/// </summary>
public static class ListParser
{
    // SIZE  DATE  TIME  "PATH"
    private static readonly Regex LineRegex = new(
        @"^\s*(\d+)\s+(\d{4}/\d{2}/\d{2})\s+(\d{2}:\d{2})\s+""(.+)""\s*$",
        RegexOptions.Compiled);

    /// <summary>Parse a single line from snapraid list output. Returns null if the line doesn't match.</summary>
    public static FileEntry? ParseLine(string line)
    {
        var m = LineRegex.Match(line);
        if (!m.Success) return null;
        if (!long.TryParse(m.Groups[1].Value, out var size)) return null;
        var dateStr = m.Groups[2].Value + " " + m.Groups[3].Value;
        DateTime.TryParseExact(dateStr, "yyyy/MM/dd HH:mm",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var date);
        return new FileEntry { SizeBytes = size, LastModified = date, RelativePath = m.Groups[4].Value };
    }

    public static List<FileEntry> Parse(string output)
    {
        var entries = new List<FileEntry>();
        if (string.IsNullOrWhiteSpace(output)) return entries;
        foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var e = ParseLine(line);
            if (e != null) entries.Add(e);
        }
        return entries;
    }

    /// <summary>
    /// Filter entries using a wildcard pattern (e.g. "*DSC_9509*", "*.jpg").
    /// Matches against the filename only (not the full path) unless the pattern contains '/'.
    /// </summary>
    public static List<FileEntry> Filter(List<FileEntry> all, string wildcard)
    {
        if (string.IsNullOrWhiteSpace(wildcard) || wildcard == "*")
            return all;

        // Convert wildcard to regex: * -> .*, ? -> .
        // If no wildcards present, treat as substring match (*pattern*)
        if (!wildcard.Contains('*') && !wildcard.Contains('?'))
            wildcard = $"*{wildcard}*";

        var regexPattern = "^" +
            Regex.Escape(wildcard)
                 .Replace(@"\*", ".*")
                 .Replace(@"\?", ".") +
            "$";
        var rx = new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Match against filename only if no path separator, otherwise full path
        bool matchFullPath = wildcard.Contains('/') || wildcard.Contains('\\');

        return all.Where(e =>
        {
            var target = matchFullPath ? e.RelativePath : e.FileName;
            return rx.IsMatch(target);
        }).ToList();
    }

    /// <summary>
    /// Build a virtual folder tree from a flat list of FileEntry records.
    /// Returns the root-level nodes (top-level folders).
    /// </summary>
    public static List<FileTreeNode> BuildTree(List<FileEntry> entries)
    {
        // Dictionary of path -> node for folder lookup
        var folderMap = new Dictionary<string, FileTreeNode>(StringComparer.OrdinalIgnoreCase);
        var roots = new List<FileTreeNode>();

        FileTreeNode GetOrCreateFolder(string path)
        {
            if (folderMap.TryGetValue(path, out var existing)) return existing;

            var node = new FileTreeNode
            {
                Name = System.IO.Path.GetFileName(path.TrimEnd('/')),
                RelativePath = path,
                IsDirectory = true
            };
            folderMap[path] = node;

            // Attach to parent
            var parent = System.IO.Path.GetDirectoryName(path.Replace('/', '\\'))
                              ?.Replace('\\', '/');

            if (string.IsNullOrEmpty(parent))
                roots.Add(node);
            else
            {
                var parentNode = GetOrCreateFolder(parent);
                parentNode.Children.Add(node);
            }

            return node;
        }

        foreach (var entry in entries.OrderBy(e => e.RelativePath))
        {
            var dir = System.IO.Path.GetDirectoryName(entry.RelativePath.Replace('/', '\\'))
                          ?.Replace('\\', '/') ?? "";

            var folder = string.IsNullOrEmpty(dir)
                ? null
                : GetOrCreateFolder(dir);

            var fileNode = new FileTreeNode
            {
                Name = entry.FileName,
                RelativePath = entry.RelativePath,
                IsDirectory = false,
                FileEntry = entry
            };

            if (folder != null)
                folder.Children.Add(fileNode);
            else
                roots.Add(fileNode);
        }

        // Compute folder stats bottom-up
        ComputeFolderStats(roots);

        return roots;
    }

    private static (int total, int ok, int missing, int bad) ComputeFolderStats(
        IEnumerable<FileTreeNode> nodes)
    {
        int total = 0, ok = 0, missing = 0, bad = 0;
        foreach (var node in nodes)
        {
            if (node.IsDirectory)
            {
                var (t, o, m, b) = ComputeFolderStats(node.Children);
                node.TotalFiles   = t;
                node.OkFiles      = o;
                node.MissingFiles = m;
                node.BadFiles     = b;
                total += t; ok += o; missing += m; bad += b;
            }
            else
            {
                total++;
                switch (node.Status)
                {
                    case FileStatus.OK:      ok++;      break;
                    case FileStatus.Missing: missing++; break;
                    case FileStatus.Bad:     bad++;     break;
                }
            }
        }
        return (total, ok, missing, bad);
    }
}
