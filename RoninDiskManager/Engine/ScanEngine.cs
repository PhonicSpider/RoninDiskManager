using System.IO;
using RoninDiskManager.Models;

namespace RoninDiskManager.Engine;

// ── Scan Engine Facade ────────────────────────────────────────────────────────
// Entry point for all scans. Detects the target volume's filesystem and
// routes to the appropriate engine:
//
//   NTFS   →  MftScanEngine   (USN journal, near-instant enumeration)
//   Other  →  FallbackScanEngine (recursive Win32 directory walk)
//
// If the MFT engine fails (e.g. insufficient privileges, unusual volume config),
// it automatically falls back with a console warning.
public class ScanEngine
{
    private readonly MftScanEngine      _mft      = new();
    private readonly FallbackScanEngine _fallback = new();

    public async Task<DiskNode> ScanAsync(
        string             rootPath,
        Action<string>     logConsole,
        IProgress<string>? progress = null,
        CancellationToken  ct       = default)
    {
        // ── Filesystem detection ──────────────────────────────────────────────
        bool isNtfs = IsNtfs(rootPath, logConsole);

        if (isNtfs)
        {
            logConsole("[Engine] NTFS volume detected — using MFT scanner.");
            try
            {
                return await _mft.ScanAsync(rootPath, logConsole, progress, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                logConsole($"[Engine] MFT scan failed ({ex.Message}) — falling back to directory walker.");
            }
        }
        else
        {
            logConsole("[Engine] Non-NTFS volume detected — using fallback directory walker.");
        }

        // ── Fallback path ─────────────────────────────────────────────────────
        return await _fallback.ScanAsync(rootPath, logConsole, progress, ct);
    }

    // ── NTFS detection ────────────────────────────────────────────────────────
    private static bool IsNtfs(string path, Action<string> logConsole)
    {
        try
        {
            var root   = Path.GetPathRoot(path) ?? path;
            var drive  = new DriveInfo(root);
            var format = drive.DriveFormat;
            logConsole($"[Engine] Volume '{root}' filesystem: {format}");
            return string.Equals(format, "NTFS", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            logConsole($"[Engine] Could not detect filesystem ({ex.Message}) — assuming non-NTFS.");
            return false;
        }
    }
}
