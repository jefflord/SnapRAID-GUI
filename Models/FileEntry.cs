namespace SnapRAIDGUI.Models;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

/// <summary>Status of a file in the SnapRAID array.</summary>
public enum FileStatus
{
    Unknown,
    OK,
    Missing,    // file tracked by parity but not on disk
    Bad,        // file on disk but data errors
    Unrecoverable
}

/// <summary>A single file entry from "snapraid list" output.</summary>
public class FileEntry
{
    /// <summary>Size in bytes.</summary>
    public long SizeBytes { get; set; }

    /// <summary>Last-modified date from the list output.</summary>
    public DateTime LastModified { get; set; }

    /// <summary>Full relative path as reported by SnapRAID (e.g. "Digital Photography/2019/DSC_9509.JPG").</summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>Just the filename portion.</summary>
    public string FileName => System.IO.Path.GetFileName(RelativePath.Replace('/', '\\'));

    /// <summary>Directory portion of the relative path.</summary>
    public string Directory => System.IO.Path.GetDirectoryName(RelativePath.Replace('/', '\\')) ?? string.Empty;

    public FileStatus Status { get; set; } = FileStatus.Unknown;

    public string SizeDisplay => SizeBytes >= 1_048_576
        ? $"{SizeBytes / 1_048_576.0:F1} MB"
        : SizeBytes >= 1024
            ? $"{SizeBytes / 1024.0:F1} KB"
            : $"{SizeBytes} B";

    public string StatusIcon => Status switch
    {
        FileStatus.OK            => "✓",
        FileStatus.Missing       => "⚠",
        FileStatus.Bad           => "✗",
        FileStatus.Unrecoverable => "💀",
        _                        => "?"
    };

    public string StatusColor => Status switch
    {
        FileStatus.OK            => "#22c55e",
        FileStatus.Missing       => "#f59e0b",
        FileStatus.Bad           => "#ef4444",
        FileStatus.Unrecoverable => "#7c3aed",
        _                        => "#94a3b8"
    };
}

/// <summary>
/// A node in the virtual file-system tree built from "snapraid list" output.
/// Can represent either a folder or a file.
/// </summary>
public partial class FileTreeNode : ObservableObject
{
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isLoading;

    public string Name { get; set; } = string.Empty;

    /// <summary>Full relative path from the array root.</summary>
    public string RelativePath { get; set; } = string.Empty;

    public bool IsDirectory { get; set; }

    /// <summary>For file nodes — the backing FileEntry.</summary>
    public FileEntry? FileEntry { get; set; }

    /// <summary>Child nodes (sub-folders and files).</summary>
    public ObservableCollection<FileTreeNode> Children { get; } = new();

    // ── Aggregated folder status ──────────────────────────────────────────
    public int TotalFiles    { get; set; }
    public int OkFiles       { get; set; }
    public int MissingFiles  { get; set; }
    public int BadFiles      { get; set; }

    public string FolderStatusSummary => IsDirectory
        ? $"{TotalFiles} files" +
          (MissingFiles > 0 ? $"  ⚠ {MissingFiles} missing" : "") +
          (BadFiles     > 0 ? $"  ✗ {BadFiles} bad"         : "")
        : string.Empty;

    public string FolderStatusColor => (MissingFiles + BadFiles) > 0 ? "#ef4444" : "#64748b";

    // ── File node display ─────────────────────────────────────────────────
    public string StatusIcon  => FileEntry?.StatusIcon  ?? "";
    public string StatusColor => FileEntry?.StatusColor ?? "#94a3b8";
    public string SizeDisplay => FileEntry?.SizeDisplay ?? "";
    public string LastModifiedDisplay => FileEntry?.LastModified.ToString("yyyy-MM-dd") ?? "";
    public FileStatus Status  => FileEntry?.Status ?? FileStatus.Unknown;
}
