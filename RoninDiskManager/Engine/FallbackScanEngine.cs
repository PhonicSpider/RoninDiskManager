using System.IO;
using RoninDiskManager.Models;

namespace RoninDiskManager.Engine;

// ── Fallback Scanner ──────────────────────────────────────────────────────────
// Standard recursive Win32 directory walker. Used for non-NTFS volumes
// (FAT32, exFAT, network shares, USB drives, optical media, etc.).
// Uses EnumerateFileSystemInfos so file sizes come back in the same
// directory-read batch — no second FileInfo call per file.
internal class FallbackScanEngine
{
    internal async Task<DiskNode> ScanAsync(
        string         rootPath,
        Action<string> logConsole,
        IProgress<string>? progress = null,
        CancellationToken  ct       = default)
    {
        return await Task.Run(() => ScanDirectory(rootPath, null, logConsole, progress, ct), ct);
    }

    // ── Recursive directory walk ──────────────────────────────────────────────
    private static DiskNode ScanDirectory(
        string             path,
        DiskNode?          parent,
        Action<string>     logConsole,
        IProgress<string>? progress,
        CancellationToken  ct)
    {
        ct.ThrowIfCancellationRequested();
        progress?.Report(path);

        var node = new DiskNode
        {
            Name        = Path.GetFileName(path) is { Length: > 0 } n ? n : path,
            FullPath    = path,
            IsDirectory = true,
            Parent      = parent
        };

        long totalSize = 0;

        try
        {
            foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos())
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (entry is FileInfo fi)
                    {
                        node.Children.Add(new DiskNode
                        {
                            Name        = fi.Name,
                            FullPath    = fi.FullName,
                            IsDirectory = false,
                            SizeBytes   = fi.Length,
                            Parent      = node
                        });
                        totalSize += fi.Length;
                    }
                    else if (entry is DirectoryInfo di)
                    {
                        var child = ScanDirectory(di.FullName, node, logConsole, progress, ct);
                        node.Children.Add(child);
                        totalSize += child.SizeBytes;
                    }
                }
                catch { }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logConsole($"[Fallback] Skipped '{path}': {ex.Message}");
        }

        node.SizeBytes = totalSize;
        return node;
    }
}
