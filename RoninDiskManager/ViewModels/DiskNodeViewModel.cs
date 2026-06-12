using CommunityToolkit.Mvvm.ComponentModel;
using RoninDiskManager.Models;
using System.Collections.ObjectModel;

namespace RoninDiskManager.ViewModels;

public partial class DiskNodeViewModel : ObservableObject
{
    private readonly DiskNode _node;
    private readonly long _rootSize;
    private bool _childrenLoaded;

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isSelected;

    public DiskNodeViewModel(DiskNode node, long rootSize)
    {
        _node = node;
        _rootSize = rootSize > 0 ? rootSize : 1;

        // Add a placeholder so the expand arrow appears on non-empty directories
        if (node.IsDirectory && node.Children.Count > 0)
            Children.Add(CreatePlaceholder());
    }

    public string Name        => _node.Name;
    public string FullPath    => _node.FullPath;
    public bool   IsDirectory => _node.IsDirectory;
    public long   SizeBytes   => _node.SizeBytes;

    /// <summary>Raw count of direct children from the scan model (not lazy-loaded VM children).</summary>
    public int DirectChildCount => _node.Children.Count;

    public string ChildCountText => _node.Children.Count switch
    {
        0 => "no items",
        1 => "1 item",
        _ => $"{_node.Children.Count:N0} items"
    };

    public string SizeDisplay
    {
        get
        {
            if (_node.SizeBytes <= 0) return string.Empty;

            string size = _node.SizeBytes switch
            {
                >= 1_073_741_824 => $"{_node.SizeBytes / 1_073_741_824.0:F1} GB",
                >= 1_048_576     => $"{_node.SizeBytes / 1_048_576.0:F0} MB",
                _                => $"{_node.SizeBytes / 1024.0:F0} KB"
            };

            double pct = _node.SizeBytes / (double)_rootSize * 100;
            return $"  [{size} / {pct:F1}%]";
        }
    }

    /// <summary>Rich multi-line tooltip shown on tree nodes.</summary>
    public string ToolTipText
    {
        get
        {
            if (_node.SizeBytes <= 0) return _node.FullPath;

            string size = _node.SizeBytes switch
            {
                >= 1_073_741_824 => $"{_node.SizeBytes / 1_073_741_824.0:F2} GB",
                >= 1_048_576     => $"{_node.SizeBytes / 1_048_576.0:F1} MB",
                _                => $"{_node.SizeBytes / 1024.0:F0} KB"
            };
            double pct = _node.SizeBytes / (double)_rootSize * 100;

            var lines = new System.Text.StringBuilder();
            lines.AppendLine(_node.FullPath);
            lines.AppendLine($"Size:   {size}  ({pct:F2}% of root)");
            if (_node.IsDirectory)
                lines.Append($"Items:  {ChildCountText}");
            else
                lines.Append($"Type:   file");
            return lines.ToString();
        }
    }

    public string Icon => _node.IsDirectory ? "📁" : "📄";

    public ObservableCollection<DiskNodeViewModel> Children { get; } = [];

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && !_childrenLoaded)
            LoadChildren();
    }

    private void LoadChildren()
    {
        _childrenLoaded = true;
        Children.Clear();

        foreach (var child in _node.Children.OrderByDescending(c => c.SizeBytes))
            Children.Add(new DiskNodeViewModel(child, _rootSize));
    }

    private static DiskNodeViewModel CreatePlaceholder()
    {
        var dummy = new DiskNode { Name = "...", FullPath = string.Empty };
        return new DiskNodeViewModel(dummy, 1);
    }
}
