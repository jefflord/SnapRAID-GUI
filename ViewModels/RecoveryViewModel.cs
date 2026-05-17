namespace SnapRAIDGUI.ViewModels;

using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SnapRAIDGUI.Models;
using SnapRAIDGUI.Services;

public partial class RecoveryViewModel : BaseViewModel
{
    private readonly SnapRAIDService _service;
    private readonly LoggingService  _logger;
    private readonly Func<bool>      _confirmOnFix; // reads live setting value

    // Cancellation for the currently running stream/search
    private CancellationTokenSource? _searchCts;

    // When results were populated by clicking a folder, this holds the folder path
    // so CheckAll can use a single wildcard check instead of per-file calls.
    private string? _selectedFolderPath;

    // All currently selected files in the results list (multi-select)
    private List<FileEntry> _selectedFiles = new();

    /// Called from code-behind whenever ListView.SelectionChanged fires.
    public void SetSelectedFiles(List<FileEntry> files)
    {
        _selectedFiles = files;
        // SelectedFile = the primary item (first selected, or null)
        SelectedFile = files.Count > 0 ? files[0] : null;
        CheckFileCommand.NotifyCanExecuteChanged();
        FixFileCommand.NotifyCanExecuteChanged();
    }

    // ── Observable state ──────────────────────────────────────────────────
    [ObservableProperty] private string    _searchPattern  = string.Empty;
    [ObservableProperty] private string    _statusText     = "Enter a search pattern and press Search.";
    [ObservableProperty] private bool      _isBusy;
    [ObservableProperty] private bool      _isSearching;   // streaming search in progress
    [ObservableProperty] private bool      _hasTreeData;
    [ObservableProperty] private string    _consoleOutput  = string.Empty;
    [ObservableProperty] private FileEntry? _selectedFile;
    [ObservableProperty] private int       _matchCount;

    public ObservableCollection<FileEntry>    SearchResults { get; } = new();
    public ObservableCollection<FileTreeNode> TreeRoots     { get; } = new();

    [ObservableProperty] private string _selectedFolderLabel = string.Empty;

    /// True when the results panel has anything to show.
    public bool HasResults => SearchResults.Count > 0;

    /// <summary>
    /// Called from the view when a folder node is clicked in the tree.
    /// Populates SearchResults with the folder's direct file children.
    /// </summary>
    public void SelectFolder(FileTreeNode folderNode)
    {
        _selectedFolderPath = folderNode.RelativePath;
        SelectedFolderLabel = folderNode.RelativePath;
        SelectedFile = null;
        SearchResults.Clear();
        MatchCount = 0;

        // Collect direct file children of this folder node
        var files = folderNode.Children
            .Where(n => !n.IsDirectory && n.FileEntry != null)
            .Select(n => n.FileEntry!)
            .ToList();

        foreach (var f in files) SearchResults.Add(f);
        MatchCount = files.Count;
        StatusText = $"Folder: {folderNode.RelativePath} — {files.Count} file(s). Click \"Check All Results\" to check.";

        CheckAllCommand.NotifyCanExecuteChanged();
    }

    // ── Search — streams snapraid list, matches lines on-the-fly ─────────
    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task Search()
    {
        var pattern = SearchPattern?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(pattern)) return;

        // Cancel any previous search
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;

        IsSearching = true;
        IsBusy      = false;
        _selectedFolderPath = null;   // results now come from search, not folder click
        SelectedFolderLabel = string.Empty;
        SearchResults.Clear();
        MatchCount  = 0;
        SelectedFile = null;
        StatusText  = $"Searching for \"{pattern}\"…";
        AppendConsole($"\nSearching: snapraid list | match \"{pattern}\"\n");

        // Compile wildcard → regex once
        var rx = WildcardToRegex(pattern);

        // Batch UI updates — add to a local list, flush to ObservableCollection periodically
        var batch     = new List<FileEntry>(64);
        var lastFlush = DateTime.UtcNow;
        int matched   = 0;
        int linesSeen = 0;

        try
        {
            await _service.StreamCommandAsync("list", line =>
            {
                if (ct.IsCancellationRequested) return;

                linesSeen++;

                var entry = ListParser.ParseLine(line);
                if (entry == null) return;

                // Match against filename or full path depending on pattern
                var target = pattern.Contains('/') || pattern.Contains('\\')
                    ? entry.RelativePath
                    : entry.FileName;

                if (!rx.IsMatch(target)) return;

                matched++;
                batch.Add(entry);

                // Flush to UI every 50ms or every 100 items
                var now = DateTime.UtcNow;
                if (batch.Count >= 100 || (now - lastFlush).TotalMilliseconds >= 50)
                {
                    var toAdd = batch.ToList();
                    batch.Clear();
                    lastFlush = now;

                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        foreach (var e in toAdd) SearchResults.Add(e);
                        MatchCount = matched;
                        StatusText = $"{matched:N0} matches so far…";
                    });
                }
            }, ct);

            // Flush remainder
            if (batch.Count > 0 && !ct.IsCancellationRequested)
            {
                var remaining = batch.ToList();
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    foreach (var e in remaining) SearchResults.Add(e);
                });
            }

            if (!ct.IsCancellationRequested)
            {
                MatchCount = matched;
                StatusText = matched == 0
                    ? $"No files found matching \"{pattern}\" (scanned {linesSeen} lines)."
                    : $"{matched:N0} files found matching \"{pattern}\".";
                AppendConsole($"Search complete: {matched} matches from {linesSeen} lines.\n");
            }
        }
        catch (OperationCanceledException) { /* user cancelled */ }
        catch (Exception ex)
        {
            StatusText = $"Search error: {ex.Message}";
            AppendConsole($"[ERROR] {ex.Message}\n");
        }
        finally
        {
            IsSearching = false;
            SearchCommand.NotifyCanExecuteChanged();
            CheckAllCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(IsSearching))]
    private void CancelSearch()
    {
        _searchCts?.Cancel();
        StatusText = "Search cancelled.";
        AppendConsole("Search cancelled.\n");
    }

    // ── Browse tree — loads ALL files (slow, explicitly requested) ────────
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task LoadTree()
    {
        IsBusy     = true;
        HasTreeData = false;
        StatusText = "Loading full file list for tree browser (this may take a while)…";
        AppendConsole("\nLoading full file list for tree browser…\n");
        TreeRoots.Clear();

        var allFiles = new List<FileEntry>(200_000);

        try
        {
            await _service.StreamCommandAsync("list", line =>
            {
                var entry = ListParser.ParseLine(line);
                if (entry != null) allFiles.Add(entry);
            });

            StatusText = $"Building tree from {allFiles.Count:N0} files…";
            var roots = await Task.Run(() => ListParser.BuildTree(allFiles));

            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                TreeRoots.Clear();
                foreach (var r in roots) TreeRoots.Add(r);
                HasTreeData = TreeRoots.Count > 0;
            });

            StatusText = $"Tree loaded: {allFiles.Count:N0} files.";
            AppendConsole($"Tree loaded: {allFiles.Count:N0} files.\n");
        }
        catch (Exception ex)
        {
            StatusText = $"Tree load error: {ex.Message}";
            AppendConsole($"[ERROR] {ex.Message}\n");
        }
        finally { IsBusy = false; }
    }

    // ── Check / Fix ───────────────────────────────────────────────────────
    [RelayCommand(CanExecute = nameof(CanActOnFile))]
    private async Task CheckFile()
    {
        var targets = _selectedFiles.Count > 0 ? _selectedFiles.ToList() : new List<FileEntry>();
        if (targets.Count == 0) return;

        IsBusy = true;
        AppendConsole($"\nChecking {targets.Count} file(s)…\n");

        try
        {
            foreach (var file in targets)
            {
                StatusText = $"Checking: {file.FileName}…";
                AppendConsole($"  {file.RelativePath}\n");
                var output = await _service.RunCheckFileAsync(file.RelativePath);
                var result = CheckResultParser.Parse(output, file.RelativePath);
                _logger.WriteLog("recovery_check", output);
                file.Status = result.Status;
                AppendConsole($"  → {result.Summary}\n");
                RefreshFileInResults(file);
            }
            var summary = targets.Count == 1
                ? $"{targets[0].FileName}: {targets[0].Status}"
                : $"Check complete: {targets.Count} files checked.";
            StatusText = summary;
        }
        catch (Exception ex)
        {
            AppendConsole($"[ERROR] {ex.Message}\n");
            StatusText = $"Check failed: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanFixFile))]
    private async Task FixFile()
    {
        var targets = _selectedFiles
            .Where(f => f.Status is FileStatus.Bad or FileStatus.Missing or FileStatus.Unrecoverable or FileStatus.Unknown)
            .ToList();
        if (targets.Count == 0) return;

        var fileList = targets.Count == 1
            ? targets[0].RelativePath
            : $"{targets.Count} files:\n" + string.Join("\n", targets.Take(10).Select(f => "  " + f.FileName))
              + (targets.Count > 10 ? $"\n  …and {targets.Count - 10} more" : "");

        if (_confirmOnFix())
        {
            var confirm = System.Windows.MessageBox.Show(
                $"Fix {(targets.Count == 1 ? "file" : "files")}:\n{fileList}\n\nThis will attempt to recover from parity.\nProceed?",
                "Confirm Fix",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);
            if (confirm != System.Windows.MessageBoxResult.Yes) return;
        }

        IsBusy = true;
        AppendConsole($"\nFixing {targets.Count} file(s)…\n");

        try
        {
            foreach (var file in targets)
            {
                StatusText = $"Fixing: {file.FileName}…";
                AppendConsole($"  {file.RelativePath}\n");
                var output = await _service.RunFixFileAsync(file.RelativePath);
                var result = CheckResultParser.Parse(output, file.RelativePath);
                _logger.WriteLog("recovery_fix", output);
                file.Status = result.WasRecovered ? FileStatus.OK : result.Status;
                AppendConsole($"  → {result.Summary}\n");
                RefreshFileInResults(file);
            }
            StatusText = $"Fix complete: {targets.Count} file(s) processed.";
        }
        catch (Exception ex)
        {
            AppendConsole($"[ERROR] {ex.Message}\n");
            StatusText = $"Fix failed: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanRunBatch))]
    private async Task CheckAll()
    {
        IsBusy = true;

        try
        {
            if (_selectedFolderPath != null)
                await CheckAllFolder(_selectedFolderPath);
            else
                await CheckAllIndividual();
        }
        finally { IsBusy = false; }
    }

    /// <summary>Single snapraid check -f "/folder/*" call — fast, one process.</summary>
    private async Task CheckAllFolder(string folderPath)
    {
        StatusText = $"Checking folder: {folderPath}/*…";
        AppendConsole($"\nChecking folder: {folderPath}/*\n");

        var output = await _service.RunCheckFolderAsync(folderPath);
        _logger.WriteLog("recovery_check_folder", output);

        var resultMap = CheckResultParser.ParseMulti(output, out var totals);

        // Apply statuses back to the FileEntry objects in SearchResults
        var files = SearchResults.ToList();
        int ok = 0, bad = 0, missing = 0, unknown = 0;

        foreach (var file in files)
        {
            if (resultMap.TryGetValue(file.RelativePath, out var r))
            {
                file.Status = r.Status;
            }
            else if (totals.IsEverythingOk)
            {
                // Not mentioned → no errors
                file.Status = FileStatus.OK;
            }

            switch (file.Status)
            {
                case FileStatus.OK:      ok++;      break;
                case FileStatus.Missing: missing++; break;
                case FileStatus.Bad:
                case FileStatus.Unrecoverable: bad++; break;
                default: unknown++; break;
            }
        }

        RefreshAllResults();
        StatusText = $"Folder check complete: {ok} OK, {missing} missing, {bad} bad, {unknown} unknown";
        AppendConsole($"Folder check done: {ok} OK, {missing} missing, {bad} bad\n");
    }

    /// <summary>Fallback: one check call per file (used when results came from search).</summary>
    private async Task CheckAllIndividual()
    {
        var files = SearchResults.ToList();
        StatusText = $"Checking {files.Count} files…";
        AppendConsole($"\nBatch check: {files.Count} files\n");

        int ok = 0, bad = 0, missing = 0;
        for (int i = 0; i < files.Count; i++)
        {
            if (!IsBusy) break;
            var file = files[i];
            StatusText = $"Checking {i + 1}/{files.Count}: {file.FileName}";
            try
            {
                var output = await _service.RunCheckFileAsync(file.RelativePath);
                var result = CheckResultParser.Parse(output, file.RelativePath);
                file.Status = result.Status;
                switch (result.Status)
                {
                    case FileStatus.OK:      ok++;      break;
                    case FileStatus.Missing: missing++; break;
                    default:                bad++;      break;
                }
            }
            catch { bad++; }
        }

        RefreshAllResults();
        StatusText = $"Check complete: {ok} OK, {missing} missing, {bad} bad";
        AppendConsole($"Batch check done: {ok} OK, {missing} missing, {bad} bad\n");
    }

    /// <summary>
    /// Called from the context menu "Show in Browser".
    /// Finds the folder node matching the file's directory, expands it, selects it,
    /// and populates the results panel — same as clicking the folder.
    /// Returns the target node (so code-behind can scroll it into view), or null.
    /// </summary>
    public FileTreeNode? ShowInBrowser(FileEntry file)
    {
        var dir = file.Directory.Replace('\\', '/');
        var node = FindFolderNode(TreeRoots, dir);
        if (node == null) return null;

        // Expand all ancestors
        ExpandToNode(TreeRoots, dir);

        node.IsExpanded = true;
        SelectFolder(node);
        return node;
    }

    private static FileTreeNode? FindFolderNode(IEnumerable<FileTreeNode> nodes, string path)
    {
        foreach (var n in nodes)
        {
            if (!n.IsDirectory) continue;
            if (string.Equals(n.RelativePath.Replace('\\','/'), path, StringComparison.OrdinalIgnoreCase))
                return n;
            var found = FindFolderNode(n.Children, path);
            if (found != null) return found;
        }
        return null;
    }

    private static bool ExpandToNode(IEnumerable<FileTreeNode> nodes, string targetPath)
    {
        foreach (var n in nodes)
        {
            if (!n.IsDirectory) continue;
            var nPath = n.RelativePath.Replace('\\', '/');
            if (string.Equals(nPath, targetPath, StringComparison.OrdinalIgnoreCase) ||
                targetPath.StartsWith(nPath + "/", StringComparison.OrdinalIgnoreCase))
            {
                n.IsExpanded = true;
                ExpandToNode(n.Children, targetPath);
                return true;
            }
        }
        return false;
    }
    public RecoveryViewModel(SnapRAIDService service, LoggingService logger, Func<bool> confirmOnFix)
    {
        _service      = service;
        _logger       = logger;
        _confirmOnFix = confirmOnFix;

        SearchResults.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasResults));
    }

    // ── CanExecute ────────────────────────────────────────────────────────
    private bool CanRun()       => !IsBusy && !IsSearching;
    private bool CanSearch()    => !IsSearching && !string.IsNullOrWhiteSpace(SearchPattern);
    private bool CanActOnFile() => !IsBusy && _selectedFiles.Count > 0;
    private bool CanFixFile()   => !IsBusy && _selectedFiles.Any(f =>
                                       f.Status is FileStatus.Missing or FileStatus.Bad
                                                or FileStatus.Unrecoverable or FileStatus.Unknown);
    private bool CanRunBatch()  => !IsBusy && !IsSearching && SearchResults.Count > 0;

    partial void OnIsBusyChanged(bool value)
    {
        LoadTreeCommand.NotifyCanExecuteChanged();
        CheckFileCommand.NotifyCanExecuteChanged();
        FixFileCommand.NotifyCanExecuteChanged();
        CheckAllCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsSearchingChanged(bool value)
    {
        SearchCommand.NotifyCanExecuteChanged();
        CancelSearchCommand.NotifyCanExecuteChanged();
        LoadTreeCommand.NotifyCanExecuteChanged();
        CheckAllCommand.NotifyCanExecuteChanged();
    }

    partial void OnSearchPatternChanged(string value) =>
        SearchCommand.NotifyCanExecuteChanged();

    partial void OnSelectedFileChanged(FileEntry? value)
    {
        CheckFileCommand.NotifyCanExecuteChanged();
        FixFileCommand.NotifyCanExecuteChanged();
    }

    // ── Helpers ───────────────────────────────────────────────────────────
    private static Regex WildcardToRegex(string pattern)
    {
        // If pattern has no wildcards, treat it as a substring match (*pattern*)
        if (!pattern.Contains('*') && !pattern.Contains('?'))
            pattern = $"*{pattern}*";

        var regexStr = "^" + Regex.Escape(pattern)
            .Replace(@"\*", ".*")
            .Replace(@"\?", ".") + "$";
        return new Regex(regexStr, RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    private void RefreshFileInResults(FileEntry file)
    {
        var idx = SearchResults.IndexOf(file);
        if (idx >= 0) { SearchResults.RemoveAt(idx); SearchResults.Insert(idx, file); }
    }

    private void RefreshAllResults()
    {
        var copy = SearchResults.ToList();
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            SearchResults.Clear();
            foreach (var f in copy) SearchResults.Add(f);
        });
    }

    private void AppendConsole(string text) =>
        System.Windows.Application.Current?.Dispatcher.Invoke(() => ConsoleOutput += text);
}
