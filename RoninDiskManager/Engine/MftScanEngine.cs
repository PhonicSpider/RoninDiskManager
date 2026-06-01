using System.IO;
using System.Runtime.InteropServices;
using RoninDiskManager.Models;

namespace RoninDiskManager.Engine;

// ── MFT Scan Engine ───────────────────────────────────────────────────────────
// NTFS-only scanner that reads the Master File Table via the USN journal
// (FSCTL_ENUM_USN_DATA). The kernel returns all file records in one large
// sequential read — the same technique WizTree uses — giving near-instant
// enumeration regardless of how many files are on the volume.
//
// Requires administrator privileges (the volume handle needs GENERIC_READ
// on the raw device path \\.\C:).
//
// Limitations:
//   - NTFS only. Non-NTFS volumes fall back to FallbackScanEngine.
//   - USN records carry no file sizes, so sizes are gathered in a second
//     parallel pass: one DirectoryInfo call per directory rather than
//     one FileInfo call per file.
internal class MftScanEngine
{
    private const int ReadBufferSize = 524_288; // 512 KB — fewer round trips to the kernel

    internal async Task<DiskNode> ScanAsync(
        string             rootPath,
        Action<string>     logConsole,
        IProgress<string>? progress = null,
        CancellationToken  ct       = default)
    {
        return await Task.Run(() => ScanInternal(rootPath, logConsole, progress, ct), ct);
    }

    // ── Top-level orchestration ───────────────────────────────────────────────
    private static DiskNode ScanInternal(
        string             rootPath,
        Action<string>     logConsole,
        IProgress<string>? progress,
        CancellationToken  ct)
    {
        var volumeRoot = Path.GetPathRoot(rootPath) ?? rootPath;
        var devicePath = $@"\\.\{volumeRoot.TrimEnd('\\')}";

        // ── Step 1: open the raw volume handle ────────────────────────────────
        logConsole($"[MFT] Opening volume: {devicePath}");

        using var volumeHandle = NativeMethods.CreateFile(
            devicePath,
            NativeMethods.GENERIC_READ,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            IntPtr.Zero,
            NativeMethods.OPEN_EXISTING,
            NativeMethods.FILE_FLAG_BACKUP_SEMANTICS,
            IntPtr.Zero);

        if (volumeHandle.IsInvalid)
            throw new InvalidOperationException(
                $"Cannot open volume {devicePath}. Make sure the app is running as Administrator.");

        // ── Step 2: enumerate every MFT record via USN journal ────────────────
        logConsole("[MFT] Enumerating file system entries via USN journal...");

        var entries = EnumerateMftEntries(volumeHandle, logConsole, ct);
        logConsole($"[MFT] USN enumeration complete — {entries.Count:N0} entries found.");
        ct.ThrowIfCancellationRequested();

        // ── Step 3: resolve full paths for every entry ────────────────────────
        logConsole("[MFT] Resolving file paths...");

        var pathCache = BuildPathCache(entries, volumeRoot, ct);
        ct.ThrowIfCancellationRequested();

        // ── Step 4: build DiskNode tree (filtered to scan root) ───────────────
        logConsole($"[MFT] Building tree under: {rootPath}");

        var (nodeMap, rootNode) = BuildTree(entries, pathCache, rootPath);
        logConsole($"[MFT] Tree built — {nodeMap.Count:N0} nodes in scan scope.");
        ct.ThrowIfCancellationRequested();

        // ── Step 5: populate file sizes via batched directory enumeration ──────
        logConsole("[MFT] Gathering file sizes (batched per directory)...");

        PopulateSizes(nodeMap, logConsole, progress, ct);
        ct.ThrowIfCancellationRequested();

        // ── Step 6: aggregate sizes bottom-up ─────────────────────────────────
        logConsole("[MFT] Aggregating directory sizes...");

        AggregateSize(rootNode);

        logConsole("[MFT] Scan complete.");
        return rootNode;
    }

    // ── USN journal enumeration ───────────────────────────────────────────────
    // Loops FSCTL_ENUM_USN_DATA until ERROR_HANDLE_EOF. Each call returns a
    // buffer of USN_RECORD_V2 structs prefixed by an 8-byte next-cursor value.
    private static Dictionary<ulong, MftEntry> EnumerateMftEntries(
        Microsoft.Win32.SafeHandles.SafeFileHandle volumeHandle,
        Action<string>    logConsole,
        CancellationToken ct)
    {
        var entries = new Dictionary<ulong, MftEntry>(500_000);

        var enumData = new NativeMethods.MFT_ENUM_DATA_V0
        {
            StartFileReferenceNumber = 0,
            LowUsn  = 0,
            HighUsn = long.MaxValue
        };

        var buffer = Marshal.AllocHGlobal(ReadBufferSize);
        try
        {
            long lastReport = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                bool ok = NativeMethods.DeviceIoControl(
                    volumeHandle,
                    NativeMethods.FSCTL_ENUM_USN_DATA,
                    ref enumData,
                    Marshal.SizeOf<NativeMethods.MFT_ENUM_DATA_V0>(),
                    buffer,
                    ReadBufferSize,
                    out uint bytesReturned,
                    IntPtr.Zero);

                if (!ok)
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err == NativeMethods.ERROR_HANDLE_EOF) break;
                    throw new InvalidOperationException($"FSCTL_ENUM_USN_DATA failed (Win32 error {err}).");
                }

                // First 8 bytes of the output are the cursor for the next call
                enumData.StartFileReferenceNumber = (ulong)Marshal.ReadInt64(buffer);

                int offset = 8;
                while (offset < (int)bytesReturned)
                {
                    var record = Marshal.PtrToStructure<NativeMethods.USN_RECORD_V2>(buffer + offset);
                    if (record.RecordLength == 0) break;

                    if (record.MajorVersion == 2 && record.FileNameLength > 0)
                    {
                        var name = Marshal.PtrToStringUni(
                            buffer + offset + record.FileNameOffset,
                            record.FileNameLength / 2);

                        if (!string.IsNullOrEmpty(name))
                        {
                            // Normalise: strip the 16-bit sequence number so all
                            // references to the same MFT record share one key.
                            var frn  = NormalizeFrn(record.FileReferenceNumber);
                            var pFrn = NormalizeFrn(record.ParentFileReferenceNumber);

                            entries[frn] = new MftEntry
                            {
                                Frn         = frn,
                                ParentFrn   = pFrn,
                                Name        = name,
                                IsDirectory = (record.FileAttributes & NativeMethods.FILE_ATTRIBUTE_DIRECTORY) != 0
                            };
                        }
                    }

                    offset += (int)record.RecordLength;
                }

                if (entries.Count - lastReport >= 200_000)
                {
                    logConsole($"[MFT]   ... {entries.Count:N0} entries enumerated");
                    lastReport = entries.Count;
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return entries;
    }

    // ── Full path resolution ──────────────────────────────────────────────────
    // Builds FRN → absolute path by walking parent chains iteratively.
    // Avoids recursion to prevent stack overflow on deeply nested trees.
    private static Dictionary<ulong, string> BuildPathCache(
        Dictionary<ulong, MftEntry> entries,
        string            volumeRoot,
        CancellationToken ct)
    {
        var cache = new Dictionary<ulong, string>(entries.Count);

        foreach (var frn in entries.Keys)
        {
            ct.ThrowIfCancellationRequested();
            if (!cache.ContainsKey(frn))
                ResolvePath(frn, entries, cache, volumeRoot);
        }

        return cache;
    }

    private static void ResolvePath(
        ulong startFrn,
        Dictionary<ulong, MftEntry> entries,
        Dictionary<ulong, string>   cache,
        string volumeRoot)
    {
        var chain   = new List<(ulong Frn, string Name)>();
        var current = startFrn;

        while (true)
        {
            if (cache.ContainsKey(current))
                break;

            if (!entries.TryGetValue(current, out var entry) || entry.ParentFrn == current)
            {
                // Self-referential parent = the NTFS volume root directory
                cache[current] = volumeRoot;
                break;
            }

            chain.Add((current, entry.Name));
            current = entry.ParentFrn;
        }

        // Build paths downward from the resolved ancestor
        var basePath = cache[current];
        for (int i = chain.Count - 1; i >= 0; i--)
        {
            basePath = Path.Combine(basePath, chain[i].Name);
            cache[chain[i].Frn] = basePath;
        }
    }

    // ── Tree construction ─────────────────────────────────────────────────────
    // Creates DiskNode objects for every entry under scanRoot, then wires up
    // parent/child using parent FRN references from each USN record.
    private static (Dictionary<ulong, DiskNode> NodeMap, DiskNode Root) BuildTree(
        Dictionary<ulong, MftEntry> entries,
        Dictionary<ulong, string>   pathCache,
        string scanRoot)
    {
        // Normalise: keep trailing backslash on drive roots (e.g. "C:\")
        // so that new DirectoryInfo("C:\") works correctly later.
        var normalizedRoot = NormalizePath(scanRoot);
        var scopePrefix    = normalizedRoot.TrimEnd('\\') + '\\';

        // ── Create a DiskNode for every entry inside the scan root ────────────
        var nodeMap = new Dictionary<ulong, DiskNode>(entries.Count / 4);
        foreach (var kv in entries)
        {
            if (!pathCache.TryGetValue(kv.Key, out var rawPath)) continue;

            var path    = NormalizePath(rawPath);
            bool inScope = string.Equals(path, normalizedRoot, StringComparison.OrdinalIgnoreCase)
                        || path.StartsWith(scopePrefix, StringComparison.OrdinalIgnoreCase);

            if (!inScope) continue;

            nodeMap[kv.Key] = new DiskNode
            {
                Name        = kv.Value.Name,
                FullPath    = path,
                IsDirectory = kv.Value.IsDirectory
            };
        }

        // ── Synthesise missing parent nodes ───────────────────────────────────
        // Two reasons a parent FRN can be absent from nodeMap:
        //   1. NTFS volume root (FRN 5) has an empty name → filtered from entries.
        //   2. NTFS metadata dirs ($Extend, etc.) never appear in the USN journal.
        //
        // Crucially, multiple distinct FRNs can resolve to the same path —
        // e.g. FRN 5 (volume root) and FRN 11 ($Extend) both anchor to "C:\".
        // We deduplicate by *path* so all FRN variants share one DiskNode,
        // otherwise FirstOrDefault picks one of several "C:\" nodes and the
        // rest of the tree is invisible.
        var pathToSyntheticNode = new Dictionary<string, DiskNode>(StringComparer.OrdinalIgnoreCase);

        foreach (var kv in entries)
        {
            if (!nodeMap.ContainsKey(kv.Key)) continue;
            var pFrn = kv.Value.ParentFrn;
            if (pFrn == kv.Key || nodeMap.ContainsKey(pFrn)) continue;
            if (!pathCache.TryGetValue(pFrn, out var pRaw)) continue;

            var pPath   = NormalizePath(pRaw);
            bool inScope = string.Equals(pPath, normalizedRoot, StringComparison.OrdinalIgnoreCase)
                        || pPath.StartsWith(scopePrefix, StringComparison.OrdinalIgnoreCase);
            if (!inScope) continue;

            if (!pathToSyntheticNode.TryGetValue(pPath, out var synNode))
            {
                var namePart = Path.GetFileName(pPath.TrimEnd('\\')) is { Length: > 0 } n ? n : pPath;
                synNode = new DiskNode { Name = namePart, FullPath = pPath, IsDirectory = true };
                pathToSyntheticNode[pPath] = synNode;
            }

            // All FRN variants that share this path point to the same DiskNode
            nodeMap[pFrn] = synNode;
        }

        // ── Wire parent/child links ───────────────────────────────────────────
        foreach (var kv in entries)
        {
            // Skip self-referential entries (the NTFS volume root has ParentFrn == Frn)
            if (kv.Value.ParentFrn == kv.Key) continue;

            if (!nodeMap.TryGetValue(kv.Key, out var child))          continue;
            if (!nodeMap.TryGetValue(kv.Value.ParentFrn, out var par)) continue;

            child.Parent = par;
            par.Children.Add(child);
        }

        // ── Locate the root node ──────────────────────────────────────────────
        var rootNode = nodeMap.Values.FirstOrDefault(n =>
            string.Equals(n.FullPath, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            ?? new DiskNode
            {
                Name        = Path.GetFileName(normalizedRoot.TrimEnd('\\')) is { Length: > 0 } s ? s : normalizedRoot,
                FullPath    = normalizedRoot,
                IsDirectory = true
            };

        return (nodeMap, rootNode);
    }

    // ── File size population ──────────────────────────────────────────────────
    // USN records carry no file sizes. Recover them by enumerating each
    // directory once — one syscall per directory returns all children with
    // sizes, instead of one syscall per file.
    private static void PopulateSizes(
        Dictionary<ulong, DiskNode> nodeMap,
        Action<string>     logConsole,
        IProgress<string>? progress,
        CancellationToken  ct)
    {
        var directories = nodeMap.Values.Where(n => n.IsDirectory).ToList();
        long processed  = 0;

        Parallel.ForEach(
            directories,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = ct },
            dir =>
            {
                progress?.Report(dir.FullPath);
                try
                {
                    var sizeMap = new DirectoryInfo(dir.FullPath)
                        .EnumerateFileSystemInfos()
                        .OfType<FileInfo>()
                        .ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);

                    foreach (var child in dir.Children)
                    {
                        if (!child.IsDirectory && sizeMap.TryGetValue(child.Name, out var fi))
                            child.SizeBytes = fi.Length;
                    }
                }
                catch (Exception) { /* inaccessible dirs (system, recycle bin, etc.) are skipped */ }

                long done = Interlocked.Increment(ref processed);
                if (done % 5_000 == 0)
                    logConsole($"[MFT]   ... {done:N0} / {directories.Count:N0} directories sized");
            });
    }

    // ── Size aggregation ──────────────────────────────────────────────────────
    // Iterative post-order traversal — avoids stack overflow on deep trees.
    private static void AggregateSize(DiskNode root)
    {
        // Two-pass: first push all nodes in pre-order, then process in reverse
        // (children before parents) to ensure correct bottom-up aggregation.
        var order = new List<DiskNode>();
        var stack = new Stack<DiskNode>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            order.Add(node);
            foreach (var child in node.Children)
                stack.Push(child);
        }

        for (int i = order.Count - 1; i >= 0; i--)
        {
            var node = order[i];
            if (!node.IsDirectory) continue;

            long total = 0;
            foreach (var child in node.Children)
                total += child.SizeBytes;
            node.SizeBytes = total;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // FRNs are 64-bit: lower 48 bits = MFT record number, upper 16 = sequence number.
    // Different entries can store different sequence numbers for the same parent, so
    // strip the sequence number to get a stable, consistent dictionary key.
    private static ulong NormalizeFrn(ulong frn) => frn & 0x0000_FFFF_FFFF_FFFF;

    // Trim trailing backslash but preserve drive roots ("C:\" stays "C:\")
    // so DirectoryInfo and path comparisons both work correctly.
    private static string NormalizePath(string path)
    {
        var trimmed = path.TrimEnd('\\');
        return trimmed.Length == 2 && trimmed[1] == ':' ? trimmed + '\\' : trimmed;
    }

    // ── Internal data model ───────────────────────────────────────────────────
    private sealed record MftEntry
    {
        public ulong  Frn         { get; init; }
        public ulong  ParentFrn   { get; init; }
        public string Name        { get; init; } = string.Empty;
        public bool   IsDirectory { get; init; }
    }
}
