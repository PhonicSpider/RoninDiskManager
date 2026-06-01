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

    public string Name => _node.Name;
    public string FullPath => _node.FullPath;
    public bool IsDirectory => _node.IsDirectory;
    public long SizeBytes => _node.SizeBytes;

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
