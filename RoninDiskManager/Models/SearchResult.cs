namespace RoninDiskManager.Models;

/// <summary>
/// A single hit returned by SearchEngine during an all-drives search.
/// </summary>
public sealed class SearchResult
{
    /// <summary>File or folder name (leaf only).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Absolute path to the item.</summary>
    public string FullPath { get; init; } = string.Empty;

    /// <summary>Drive root that was searched (e.g. "C:\").</summary>
    public string DriveRoot { get; init; } = string.Empty;

    /// <summary>True when the hit is a directory; false for a file.</summary>
    public bool IsDirectory { get; init; }

    /// <summary>File size in bytes. 0 for directories or inaccessible files.</summary>
    public long SizeBytes { get; init; }

    /// <summary>Human-readable file size string.</summary>
    public string SizeDisplay => SizeBytes switch
    {
        <= 0             => string.Empty,
        >= 1_073_741_824 => $"{SizeBytes / 1_073_741_824.0:F1} GB",
        >= 1_048_576     => $"{SizeBytes / 1_048_576.0:F0} MB",
        _                => $"{SizeBytes / 1024.0:F0} KB"
    };

    /// <summary>Last-modified timestamp. MinValue when unavailable.</summary>
    public DateTime DateModified { get; init; }

    /// <summary>
    /// File type description — the extension in upper case (e.g. "TXT File"),
    /// "File Folder" for directories, or "File" when there is no extension.
    /// </summary>
    public string FileType { get; init; } = string.Empty;
}
