using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.IO;
using RoninDiskManager.Engine;
using RoninDiskManager.Models;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Windows;

namespace RoninDiskManager.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ScanEngine   _engine       = new();
    private readonly SearchEngine _searchEngine = new();

    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _searchCts;

    // ── Scan ──────────────────────────────────────────────────────────────────
    [ObservableProperty] private string _scanPath = @"C:\";
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _scanStatus = "Enter a path and click Scan.";
    [ObservableProperty] private ObservableCollection<DiskNodeViewModel> _treeRoots = [];
    [ObservableProperty] private DiskNodeViewModel? _selectedNode;
    [ObservableProperty] private string _consoleText = string.Empty;

    // ── Scan stats (populated after each scan) ────────────────────────────────
    private int _totalScannedFiles;
    private int _totalScannedDirs;

    // ── Treemap ───────────────────────────────────────────────────────────────
    /// <summary>Raw scan root forwarded to the TreemapControl for rendering.</summary>
    [ObservableProperty] private DiskNode? _treemapRoot;

    /// <summary>
    /// Written by TreemapControl via OneWayToSource when the user clicks a tile;
    /// bridges into the existing <see cref="SelectedNode"/> flow.
    /// </summary>
    [ObservableProperty] private DiskNode? _treemapSelectedNode;

    partial void OnTreemapSelectedNodeChanged(DiskNode? value)
    {
        if (value == null) return;
        long rootSize = TreemapRoot?.SizeBytes ?? 1;
        SelectedNode = new DiskNodeViewModel(value, Math.Max(1, rootSize));
    }

    // ── Search ────────────────────────────────────────────────────────────────
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private ObservableCollection<SearchResult> _searchResults = [];
    [ObservableProperty] private SearchResult? _selectedSearchResult;

    // ── Unified input bar ─────────────────────────────────────────────────────
    [ObservableProperty] private string _inputQuery = @"C:\";
    [ObservableProperty] private bool _isShowingSearchResults;

    /// <summary>True while either a scan or search is in progress — drives Cancel button.</summary>
    public bool IsOperationRunning => IsScanning || IsSearching;

    // ── Status text (shown in top bar row 1) ──────────────────────────────────
    private string _lastStatus = "Enter a path and click Scan or Search.";
    public string StatusText => IsScanning ? ScanStatus : _lastStatus;

    // ── Status bar (bottom strip — selected item or scan summary) ─────────────
    public string StatusBarText
    {
        get
        {
            if (SelectedNode != null)
            {
                var n = SelectedNode;
                string sizeRaw = n.SizeBytes switch
                {
                    >= 1_073_741_824 => $"{n.SizeBytes / 1_073_741_824.0:F1} GB",
                    >= 1_048_576     => $"{n.SizeBytes / 1_048_576.0:F0} MB",
                    _                => $"{n.SizeBytes / 1024.0:F0} KB"
                };
                string pct    = TreemapRoot != null
                    ? $"  ·  {n.SizeBytes / (double)Math.Max(1, TreemapRoot.SizeBytes) * 100:F1}% of root"
                    : string.Empty;
                string kids   = n.IsDirectory ? $"  ·  {n.ChildCountText}" : string.Empty;
                return $"{n.Icon}  {n.FullPath}   ·   {sizeRaw}{pct}{kids}";
            }

            if (TreemapRoot != null)
            {
                string total = FormatBytes(TreemapRoot.SizeBytes);
                string stats = _totalScannedFiles > 0
                    ? $"  ·  {_totalScannedFiles:N0} files  ·  {_totalScannedDirs:N0} dirs"
                    : string.Empty;
                return $"📂  {TreemapRoot.FullPath}   ·   {total}{stats}";
            }

            return "Ready — enter a path and click Scan.";
        }
    }

    // ── Action mode ───────────────────────────────────────────────────────────
    [ObservableProperty] private bool _isMoveMode = true;
    [ObservableProperty] private bool _isDeleteMode;

    public string ExecuteButtonText => IsMoveMode ? "▶   Execute Move" : "▶   Execute Delete";

    // ── Move flags ────────────────────────────────────────────────────────────
    [ObservableProperty] private string _destination = string.Empty;
    [ObservableProperty] private bool _moveForce = true;
    [ObservableProperty] private bool _moveWhatIf;
    [ObservableProperty] private bool _moveVerbose = true;
    [ObservableProperty] private bool _moveNeverOverwrite;
    [ObservableProperty] private bool _moveLiteralPath;
    [ObservableProperty] private string _moveFilter = string.Empty;
    [ObservableProperty] private string _moveInclude = string.Empty;
    [ObservableProperty] private string _moveExclude = string.Empty;

    // ── Delete flags ──────────────────────────────────────────────────────────
    [ObservableProperty] private bool _deleteForce = true;
    [ObservableProperty] private bool _deleteRecurse = true;
    [ObservableProperty] private bool _deleteWhatIf;
    [ObservableProperty] private bool _deleteVerbose = true;
    [ObservableProperty] private bool _deleteLiteralPath;
    [ObservableProperty] private string _deleteFilter = string.Empty;
    [ObservableProperty] private string _deleteInclude = string.Empty;
    [ObservableProperty] private string _deleteExclude = string.Empty;

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task ScanAsync()
    {
        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();

        // Sync unified bar → ScanPath, clear previous search state
        ScanPath = InputQuery;
        IsShowingSearchResults = false;
        SearchResults.Clear();

        IsScanning = true;
        OnPropertyChanged(nameof(IsOperationRunning));
        TreeRoots.Clear();
        SelectedNode = null;

        var progress = new Progress<string>(msg =>
        {
            ScanStatus = $"Scanning  {Path.GetFileName(msg) ?? msg}";
            OnPropertyChanged(nameof(StatusText));
        });

        AppendConsole($"▶  Scanning: {ScanPath}");
        var sw = Stopwatch.StartNew();

        try
        {
            var root = await _engine.ScanAsync(ScanPath, AppendConsole, progress, _scanCts.Token);
            sw.Stop();

            // Count files and dirs for status bar
            CountNodes(root, out _totalScannedFiles, out _totalScannedDirs);

            TreeRoots.Add(new DiskNodeViewModel(root, root.SizeBytes));
            TreemapRoot = root;
            ScanStatus = $"Done — {FormatBytes(root.SizeBytes)} in {sw.Elapsed.TotalSeconds:F1}s  ·  {_totalScannedFiles:N0} files  ·  {_totalScannedDirs:N0} dirs";
            AppendConsole($"✔  Scan complete in {sw.Elapsed.TotalSeconds:F1}s — {FormatBytes(root.SizeBytes)} total\n");
            _lastStatus = ScanStatus;
            OnPropertyChanged(nameof(StatusBarText));
        }
        catch (OperationCanceledException)
        {
            ScanStatus = "Scan cancelled.";
            _lastStatus = ScanStatus;
            AppendConsole("✖  Scan cancelled.\n");
        }
        catch (Exception ex)
        {
            ScanStatus = $"Error: {ex.Message}";
            _lastStatus = ScanStatus;
            AppendConsole($"✖  {ex.Message}\n");
        }
        finally
        {
            IsScanning = false;
            OnPropertyChanged(nameof(IsOperationRunning));
            OnPropertyChanged(nameof(StatusText));
        }
    }

    [RelayCommand]
    private void CancelScan()
    {
        _scanCts?.Cancel();
        _searchCts?.Cancel();
    }

    [RelayCommand]
    private void BrowseScanPath()
    {
        var dlg = new OpenFolderDialog();
        if (dlg.ShowDialog() == true)
        {
            ScanPath = dlg.FolderName;
            InputQuery = dlg.FolderName;
        }
    }

    [RelayCommand]
    private void BrowseDestination()
    {
        var dlg = new OpenFolderDialog();
        if (dlg.ShowDialog() == true)
            Destination = dlg.FolderName;
    }

    [RelayCommand]
    private void SetMoveMode()
    {
        IsMoveMode = true;
        IsDeleteMode = false;
        OnPropertyChanged(nameof(ExecuteButtonText));
    }

    [RelayCommand]
    private void SetDeleteMode()
    {
        IsDeleteMode = true;
        IsMoveMode = false;
        OnPropertyChanged(nameof(ExecuteButtonText));
    }

    [RelayCommand(CanExecute = nameof(CanExecuteAction))]
    private async Task ExecuteActionAsync()
    {
        if (SelectedNode == null) return;

        if (IsMoveMode)
        {
            if (string.IsNullOrWhiteSpace(Destination))
            {
                AppendConsole("✖  Destination is required for Move.\n");
                return;
            }

            if (MoveNeverOverwrite)
            {
                var destPath = Path.Combine(Destination, Path.GetFileName(SelectedNode.FullPath));
                if (Path.Exists(destPath))
                {
                    AppendConsole($"✖  Aborted: '{destPath}' already exists and NeverOverwrite is enabled.\n");
                    return;
                }
            }

            await RunPowerShellAsync(BuildMoveCommand());
        }
        else
        {
            var result = MessageBox.Show(
                $"Permanently delete:\n\n{SelectedNode.FullPath}\n\nThis cannot be undone.",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (result != MessageBoxResult.Yes) return;

            await RunPowerShellAsync(BuildDeleteCommand());
        }
    }

    private bool CanExecuteAction() => SelectedNode != null;

    partial void OnSelectedNodeChanged(DiskNodeViewModel? value)
    {
        ExecuteActionCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(StatusBarText));
    }

    partial void OnSelectedSearchResultChanged(SearchResult? value)
    {
        if (value == null) return;
        var node = new DiskNode
        {
            Name        = value.Name,
            FullPath    = value.FullPath,
            IsDirectory = value.IsDirectory,
            SizeBytes   = value.SizeBytes
        };
        SelectedNode = new DiskNodeViewModel(node, value.SizeBytes > 0 ? value.SizeBytes : 1);
    }

    // ── Search commands ───────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SearchAllDrivesAsync()
    {
        // Cancel any in-flight search first
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;

        var query = InputQuery?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query))
        {
            _lastStatus = "Please enter a search term.";
            OnPropertyChanged(nameof(StatusText));
            return;
        }

        IsSearching = true;
        IsShowingSearchResults = true;
        OnPropertyChanged(nameof(IsOperationRunning));
        SearchResults.Clear();
        SelectedNode = null;
        _lastStatus = $"Searching all drives for  \"{query}\"…";
        OnPropertyChanged(nameof(StatusText));
        AppendConsole($"🔍  Search started: \"{query}\"\n");

        var progress = new Progress<string>(msg =>
        {
            _lastStatus = msg;
            OnPropertyChanged(nameof(StatusText));
            AppendConsole($"    {msg}\n");
        });

        var sw = Stopwatch.StartNew();

        try
        {
            var results = await _searchEngine.SearchAsync(query, progress, ct);
            sw.Stop();

            // Populate observable collection on the UI thread
            Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (var r in results)
                    SearchResults.Add(r);
            });

            _lastStatus = results.Count == 0
                ? $"No results found for  \"{query}\"."
                : $"{results.Count:N0} result{(results.Count == 1 ? "" : "s")} for  \"{query}\"  — {sw.Elapsed.TotalSeconds:F1}s";
            AppendConsole($"✔  {_lastStatus}\n");
        }
        catch (OperationCanceledException)
        {
            _lastStatus = "Search cancelled.";
            AppendConsole("✖  Search cancelled.\n");
        }
        catch (Exception ex)
        {
            _lastStatus = $"Search error: {ex.Message}";
            AppendConsole($"✖  Search error: {ex.Message}\n");
        }
        finally
        {
            IsSearching = false;
            OnPropertyChanged(nameof(IsOperationRunning));
            OnPropertyChanged(nameof(StatusText));
        }
    }

    [RelayCommand]
    private void CancelSearch() => _searchCts?.Cancel();

    // ── Command builders ──────────────────────────────────────────────────────

    private string BuildMoveCommand()
    {
        var sb = new StringBuilder();
        sb.Append(MoveLiteralPath
            ? $"Move-Item -LiteralPath '{EscapePs(SelectedNode!.FullPath)}'"
            : $"Move-Item -Path '{EscapePs(SelectedNode!.FullPath)}'");
        sb.Append($" -Destination '{EscapePs(Destination)}'");
        if (MoveForce)   sb.Append(" -Force");
        if (MoveVerbose) sb.Append(" -Verbose");
        if (MoveWhatIf)  sb.Append(" -WhatIf");
        if (!string.IsNullOrWhiteSpace(MoveFilter))  sb.Append($" -Filter '{MoveFilter}'");
        if (!string.IsNullOrWhiteSpace(MoveInclude)) sb.Append($" -Include {MoveInclude}");
        if (!string.IsNullOrWhiteSpace(MoveExclude)) sb.Append($" -Exclude {MoveExclude}");
        return sb.ToString();
    }

    private string BuildDeleteCommand()
    {
        var sb = new StringBuilder();
        sb.Append(DeleteLiteralPath
            ? $"Remove-Item -LiteralPath '{EscapePs(SelectedNode!.FullPath)}'"
            : $"Remove-Item -Path '{EscapePs(SelectedNode!.FullPath)}'");
        if (DeleteForce)   sb.Append(" -Force");
        if (DeleteRecurse) sb.Append(" -Recurse");
        if (DeleteVerbose) sb.Append(" -Verbose");
        if (DeleteWhatIf)  sb.Append(" -WhatIf");
        if (!string.IsNullOrWhiteSpace(DeleteFilter))  sb.Append($" -Filter '{DeleteFilter}'");
        if (!string.IsNullOrWhiteSpace(DeleteInclude)) sb.Append($" -Include {DeleteInclude}");
        if (!string.IsNullOrWhiteSpace(DeleteExclude)) sb.Append($" -Exclude {DeleteExclude}");
        return sb.ToString();
    }

    // ── Execution ─────────────────────────────────────────────────────────────

    private async Task RunPowerShellAsync(string command)
    {
        AppendConsole($"> {command}\n");

        // Use EncodedCommand to avoid quoting issues
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NonInteractive -EncodedCommand {encoded}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        try
        {
            using var proc = Process.Start(psi)!;
            proc.OutputDataReceived += (_, e) => { if (e.Data != null) AppendConsole(e.Data); };
            proc.ErrorDataReceived  += (_, e) => { if (e.Data != null) AppendConsole(e.Data); };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            await proc.WaitForExitAsync();
            AppendConsole(proc.ExitCode == 0
                ? "\n✔  Operation completed successfully.\n"
                : $"\n✖  Exited with code {proc.ExitCode}.\n");
        }
        catch (Exception ex)
        {
            AppendConsole($"\n✖  Failed to start PowerShell: {ex.Message}\n");
        }
    }

    // ── Context-menu commands ─────────────────────────────────────────────────

    [RelayCommand]
    private void OpenInExplorer()
    {
        var path = SelectedNode?.FullPath;
        if (string.IsNullOrEmpty(path)) return;
        // For files, open the containing folder and select the file
        string args = SelectedNode!.IsDirectory
            ? $"\"{path}\""
            : $"/select,\"{path}\"";
        Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
    }

    [RelayCommand]
    private void CopyPath()
    {
        var path = SelectedNode?.FullPath;
        if (!string.IsNullOrEmpty(path))
            Clipboard.SetText(path);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void AppendConsole(string text)
        => Application.Current.Dispatcher.BeginInvoke(() => ConsoleText += text + "\n");

    private static string EscapePs(string path) => path.Replace("'", "''");

    /// <summary>Counts files and directories in the scan tree iteratively.</summary>
    private static void CountNodes(DiskNode root, out int files, out int dirs)
    {
        files = 0; dirs = 0;
        var stack = new Stack<DiskNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n.IsDirectory) dirs++; else files++;
            foreach (var c in n.Children) stack.Push(c);
        }
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
        >= 1_048_576     => $"{bytes / 1_048_576.0:F0} MB",
        _                => $"{bytes / 1024.0:F0} KB"
    };
}
