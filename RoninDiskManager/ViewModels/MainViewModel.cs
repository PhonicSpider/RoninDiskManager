using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.IO;
using RoninDiskManager.Engine;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Windows;

namespace RoninDiskManager.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ScanEngine _engine = new();
    private CancellationTokenSource? _scanCts;

    // ── Scan ──────────────────────────────────────────────────────────────────
    [ObservableProperty] private string _scanPath = @"C:\";
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _scanStatus = "Enter a path and click Scan.";
    [ObservableProperty] private ObservableCollection<DiskNodeViewModel> _treeRoots = [];
    [ObservableProperty] private DiskNodeViewModel? _selectedNode;
    [ObservableProperty] private string _consoleText = string.Empty;

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

        IsScanning = true;
        TreeRoots.Clear();
        SelectedNode = null;

        var progress = new Progress<string>(msg =>
            ScanStatus = $"Scanning  {Path.GetFileName(msg) ?? msg}");

        AppendConsole($"▶  Scanning: {ScanPath}");
        var sw = Stopwatch.StartNew();

        try
        {
            var root = await _engine.ScanAsync(ScanPath, AppendConsole, progress, _scanCts.Token);
            sw.Stop();

            TreeRoots.Add(new DiskNodeViewModel(root, root.SizeBytes));
            ScanStatus = $"Done — {FormatBytes(root.SizeBytes)} scanned in {sw.Elapsed.TotalSeconds:F1}s";
            AppendConsole($"✔  Scan complete in {sw.Elapsed.TotalSeconds:F1}s — {FormatBytes(root.SizeBytes)} total\n");
        }
        catch (OperationCanceledException)
        {
            ScanStatus = "Scan cancelled.";
            AppendConsole("✖  Scan cancelled.\n");
        }
        catch (Exception ex)
        {
            ScanStatus = $"Error: {ex.Message}";
            AppendConsole($"✖  {ex.Message}\n");
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private void CancelScan() => _scanCts?.Cancel();

    [RelayCommand]
    private void BrowseScanPath()
    {
        var dlg = new OpenFolderDialog();
        if (dlg.ShowDialog() == true)
            ScanPath = dlg.FolderName;
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
        => ExecuteActionCommand.NotifyCanExecuteChanged();

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

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void AppendConsole(string text)
        => Application.Current.Dispatcher.BeginInvoke(() => ConsoleText += text + "\n");

    private static string EscapePs(string path) => path.Replace("'", "''");

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
        >= 1_048_576     => $"{bytes / 1_048_576.0:F0} MB",
        _                => $"{bytes / 1024.0:F0} KB"
    };
}
