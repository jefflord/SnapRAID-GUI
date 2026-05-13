namespace SnapRAIDGUI.Services;

using SnapRAIDGUI.Models;

public static class DiffParser
{
    public static DiffData Parse(string output)
    {
        var data = new DiffData { RawOutput = output };

        foreach (var line in output.Split('\n', '\r'))
        {
            var trimmed = line.Trim();

            // Count categories from summary section
            if (trimmed.StartsWith("added:", StringComparison.OrdinalIgnoreCase))
                data.AddedFiles = ExtractCount(trimmed);
            else if (trimmed.StartsWith("removed:", StringComparison.OrdinalIgnoreCase))
                data.RemovedFiles = ExtractCount(trimmed);
            else if (trimmed.StartsWith("updated:", StringComparison.OrdinalIgnoreCase))
                data.UpdatedFiles = ExtractCount(trimmed);
            else if (trimmed.StartsWith("moved:", StringComparison.OrdinalIgnoreCase))
                data.MovedFiles = ExtractCount(trimmed);
            else if (trimmed.StartsWith("copied:", StringComparison.OrdinalIgnoreCase))
                data.CopiedFiles = ExtractCount(trimmed);
            else if (trimmed.StartsWith("relocated:", StringComparison.OrdinalIgnoreCase))
                data.RelocatedFiles = ExtractCount(trimmed);
            else if (trimmed.StartsWith("restored:", StringComparison.OrdinalIgnoreCase))
                data.RestoredFiles = ExtractCount(trimmed);
            else if (trimmed.StartsWith("equal:", StringComparison.OrdinalIgnoreCase))
                data.EqualFiles = ExtractCount(trimmed);

            // Capture removed file paths (lines starting with " -" or similar in diff output)
            if (data.RemovedFiles > 0 && trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                var filePath = trimmed.Substring(2).Trim();
                if (!string.IsNullOrEmpty(filePath) && !data.RemovedFileList.Contains(filePath))
                    data.RemovedFileList.Add(filePath);
            }

            // Also capture from "removed files:" section headers
            if (trimmed.StartsWith("removed", StringComparison.OrdinalIgnoreCase) && trimmed.Contains("file"))
            {
                // This is a header line, skip counting
            }
        }

        return data;
    }

    private static int ExtractCount(string line)
    {
        var colonIdx = line.IndexOf(':');
        if (colonIdx >= 0 && colonIdx + 1 < line.Length)
        {
            var numStr = line.Substring(colonIdx + 1).Trim();
            // Remove any trailing text after the number
            var endIdx = 0;
            for (var i = 0; i < numStr.Length; i++)
            {
                if (!char.IsDigit(numStr[i]) && numStr[i] != ',')
                {
                    endIdx = i;
                    break;
                }
                endIdx = i + 1;
            }
            var cleanNum = numStr.Substring(0, endIdx).Replace(",", "");
            if (int.TryParse(cleanNum, out var count))
                return count;
        }
        return 0;
    }
}
