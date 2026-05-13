namespace SnapRAIDGUI.Models;

public class DiffData
{
    public int AddedFiles { get; set; }
    public int RemovedFiles { get; set; }
    public int UpdatedFiles { get; set; }
    public int MovedFiles { get; set; }
    public int CopiedFiles { get; set; }
    public int RelocatedFiles { get; set; }
    public int RestoredFiles { get; set; }
    public int EqualFiles { get; set; }
    public List<string> RemovedFileList { get; set; } = new();
    public string RawOutput { get; set; } = string.Empty;

    public bool HasLargeDeletions(int threshold) => RemovedFiles > threshold;
}
