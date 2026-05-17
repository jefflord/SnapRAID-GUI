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

    // Cancellation for the currently running stream/search
    private CancellationTokenSource? _searchCts;

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
        IsBusy      = false; // search doesn't block check/fix buttons
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
        if (SelectedFile == null) return;

        IsBusy = true;
        StatusText = $"Checking: {SelectedFile.FileName}…";
        AppendConsole($"\nChecking: {SelectedFile.RelativePath}\n");

        try
        {
            var output = await _service.RunCheckFileAsync(SelectedFile.RelativePath);
            var result = CheckResultParser.Parse(output, SelectedFile.RelativePath);
            _logger.WriteLog("recovery_check", output);

            SelectedFile.Status = result.Status;
            AppendConsole($"Result: {result.Summary}\n");
            StatusText = $"{SelectedFile.FileName}: {result.Summary}";
            RefreshFileInResults(SelectedFile);
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
        if (SelectedFile == null) return;

        var confirm = System.Windows.MessageBox.Show(
            $"Fix file:\n{SelectedFile.RelativePath}\n\nThis will attempt to recover the file from parity.\nProceed?",
            "Confirm Fix",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        IsBusy = true;
        StatusText = $"Fixing: {SelectedFile.FileName}…";
        AppendConsole($"\nFixing: {SelectedFile.RelativePath}\n");

        try
        {
            var output = await _service.RunFixFileAsync(SelectedFile.RelativePath);
            var result = CheckResultParser.Parse(output, SelectedFile.RelativePath);
            _logger.WriteLog("recovery_fix", output);

            SelectedFile.Status = result.WasRecovered ? FileStatus.OK : result.Status;
            AppendConsole($"Result: {result.Summary}\n");
            StatusText = $"{SelectedFile.FileName}: {result.Summary}";
            RefreshFileInResults(SelectedFile);
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
        var files = SearchResults.ToList();
        StatusText = $"Checking {files.Count} files…";
        AppendConsole($"\nBatch check: {files.Count} files\n");

        int ok = 0, bad = 0, missing = 0;
        for (int i = 0; i < files.Count; i++)
        {
            if (!IsBusy) break; // cancelled externally
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
        IsBusy = false;
    }

    // ── Constructor ───────────────────────────────────────────────────────
    public RecoveryViewModel(SnapRAIDService service, LoggingService logger)
    {
        _service = service;
        _logger  = logger;
    }

    // ── CanExecute ────────────────────────────────────────────────────────
    private bool CanRun()       => !IsBusy && !IsSearching;
    private bool CanSearch()    => !IsSearching && !string.IsNullOrWhiteSpace(SearchPattern);
    private bool CanActOnFile() => !IsBusy && SelectedFile != null;
    private bool CanFixFile()   => !IsBusy && SelectedFile != null &&
                                   SelectedFile.Status is FileStatus.Missing or FileStatus.Bad;
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
