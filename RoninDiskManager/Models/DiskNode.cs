namespace RoninDiskManager.Models;

public class DiskNode
{
    public string Name { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public bool IsDirectory { get; init; }
    public long SizeBytes { get; set; }
    public DiskNode? Parent { get; set; }
    public List<DiskNode> Children { get; } = [];
}
