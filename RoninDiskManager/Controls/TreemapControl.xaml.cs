using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RoninDiskManager.Models;

namespace RoninDiskManager.Controls;

/// <summary>
/// Squarified treemap control.  Shows the children of <see cref="RootNode"/> as
/// proportionally-sized tiles.  Double-click a directory tile to drill in;
/// use the breadcrumb bar or the Back button to navigate back up.
/// </summary>
public partial class TreemapControl : UserControl
{
    // ── Dependency Properties ──────────────────────────────────────────────

    public static readonly DependencyProperty RootNodeProperty =
        DependencyProperty.Register(nameof(RootNode), typeof(DiskNode), typeof(TreemapControl),
            new PropertyMetadata(null, OnRootNodeChanged));

    public DiskNode? RootNode
    {
        get => (DiskNode?)GetValue(RootNodeProperty);
        set => SetValue(RootNodeProperty, value);
    }

    /// <summary>
    /// Set by the control when the user clicks a tile.  Bind with
    /// <c>Mode=OneWayToSource</c> to push selections back to the ViewModel.
    /// </summary>
    public static readonly DependencyProperty SelectedDiskNodeProperty =
        DependencyProperty.Register(nameof(SelectedDiskNode), typeof(DiskNode), typeof(TreemapControl),
            new PropertyMetadata(null));

    public DiskNode? SelectedDiskNode
    {
        get => (DiskNode?)GetValue(SelectedDiskNodeProperty);
        set => SetValue(SelectedDiskNodeProperty, value);
    }

    // ── Navigation state ──────────────────────────────────────────────────

    private DiskNode?            _displayRoot;
    private readonly Stack<DiskNode> _navStack = new();

    // ── Ronin colour palette (10 distinct dark hues) ──────────────────────

    private static readonly Color[] Palette =
    [
        Color.FromRgb(0x6A, 0x10, 0x10), // dark crimson
        Color.FromRgb(0x28, 0x4A, 0x58), // dark teal
        Color.FromRgb(0x5C, 0x28, 0x08), // dark burnt-orange
        Color.FromRgb(0x18, 0x3A, 0x28), // dark forest
        Color.FromRgb(0x40, 0x18, 0x48), // dark purple
        Color.FromRgb(0x18, 0x28, 0x58), // dark navy
        Color.FromRgb(0x50, 0x38, 0x08), // dark amber
        Color.FromRgb(0x48, 0x10, 0x2A), // dark rose
        Color.FromRgb(0x20, 0x48, 0x20), // dark moss
        Color.FromRgb(0x30, 0x20, 0x60), // dark indigo
    ];

    // ── Constructor ────────────────────────────────────────────────────────

    public TreemapControl()
    {
        InitializeComponent();
    }

    // ── Root changed (from binding) ────────────────────────────────────────

    private static void OnRootNodeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctrl = (TreemapControl)d;
        ctrl._navStack.Clear();
        ctrl._displayRoot = (DiskNode?)e.NewValue;
        ctrl.UpdateBreadcrumb();
        ctrl.Render();
    }

    // ── Canvas size change ─────────────────────────────────────────────────

    private void TreeCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => Render();

    // ── Back button ────────────────────────────────────────────────────────

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_navStack.Count > 0)
        {
            _displayRoot = _navStack.Pop();
            UpdateBreadcrumb();
            Render();
        }
    }

    // ── Render ─────────────────────────────────────────────────────────────

    private void Render()
    {
        TreeCanvas.Children.Clear();

        BackButton.IsEnabled = _navStack.Count > 0;

        double cw = TreeCanvas.ActualWidth;
        double ch = TreeCanvas.ActualHeight;

        if (_displayRoot == null || cw < 8 || ch < 8)
        {
            EmptyLabel.Visibility = Visibility.Visible;
            return;
        }

        EmptyLabel.Visibility = Visibility.Collapsed;

        try
        {
            var bounds = new Rect(2, 2, cw - 4, ch - 4);
            var tiles  = ComputeLayout(_displayRoot, bounds);

            for (int i = 0; i < tiles.Count; i++)
            {
                var (node, rect) = tiles[i];
                // Multi-level: expand directories into their children so we see
                // many small blocks instead of a few giant ones.
                RenderLevel(node, rect, Palette[i % Palette.Length], 0);
            }
        }
        catch (Exception ex)
        {
            ShowRenderError(ex);
        }
    }

    private void ShowRenderError(Exception ex)
    {
        TreeCanvas.Children.Clear();
        var tb = new TextBlock
        {
            Text        = $"⚠  Render error ({ex.GetType().Name}):\n{ex.Message}",
            Foreground  = new SolidColorBrush(Color.FromRgb(0xE0, 0x40, 0x40)),
            Background  = new SolidColorBrush(Color.FromRgb(0x10, 0x05, 0x05)),
            Padding     = new Thickness(14),
            TextWrapping = TextWrapping.Wrap,
            FontFamily  = new FontFamily("Consolas"),
            FontSize    = 11,
            MaxWidth    = TreeCanvas.ActualWidth - 20,
        };
        Canvas.SetLeft(tb, 10);
        Canvas.SetTop(tb,  10);
        TreeCanvas.Children.Add(tb);
    }

    // ── Multi-level rendering ──────────────────────────────────────────────

    /// <summary>
    /// How many levels deep the renderer auto-expands directories.
    /// 0 = depth-1 only (old behaviour); 2 = shows grandchildren.
    /// </summary>
    private const int  MaxRenderDepth   = 2;
    private const double MinExpandWidth  = 48;
    private const double MinExpandHeight = 48;

    /// <summary>
    /// Render a node recursively: directories expand into their children so
    /// you see many small tiles instead of a handful of giant blocks.
    /// </summary>
    private void RenderLevel(DiskNode node, Rect rect, Color color, int depth)
    {
        if (!double.IsFinite(rect.Width) || !double.IsFinite(rect.Height) ||
            rect.Width < 2 || rect.Height < 2) return;

        bool expand = node.IsDirectory
                      && node.Children.Count > 0
                      && depth < MaxRenderDepth
                      && rect.Width  >= MinExpandWidth
                      && rect.Height >= MinExpandHeight;

        if (!expand)
        {
            RenderTile(node, rect, color);
            return;
        }

        // Draw the directory as a dark shell + thin name bar
        DrawDirShell(node, rect, color);

        // Children fill the interior (below the name bar)
        double hdrH = rect.Height >= 22 ? 16 : 0;
        const double pad = 1;
        var inner = new Rect(
            rect.X + pad,
            rect.Y + hdrH + pad,
            Math.Max(0, rect.Width  - 2 * pad),
            Math.Max(0, rect.Height - hdrH - 2 * pad));

        if (inner.Width < 4 || inner.Height < 4) return;

        var childTiles = ComputeLayout(node, inner);
        // Each depth level gets a lighter shade so parent/child boundaries read clearly
        Color childColor = LightenColor(color, 18 + depth * 10);

        for (int i = 0; i < childTiles.Count; i++)
        {
            var (child, childRect) = childTiles[i];
            RenderLevel(child, childRect, childColor, depth + 1);
        }
    }

    /// <summary>
    /// Draws a directory's background shell and a thin label bar directly on the canvas.
    /// Children will be layered on top of this by the caller.
    /// </summary>
    private void DrawDirShell(DiskNode node, Rect rect, Color color)
    {
        double w = Math.Max(0, rect.Width);
        double h = Math.Max(0, rect.Height);

        var shell = new Border
        {
            Width           = w,
            Height          = h,
            Background      = new SolidColorBrush(DarkenColor(color, 28)),
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0x08, 0x08, 0x08)),
            BorderThickness = new Thickness(1),
            ClipToBounds    = true,
            ToolTip         = $"📁 {node.Name}\n{FormatSize(node.SizeBytes)}\n{node.FullPath}\nDouble-click to drill in",
        };

        shell.MouseLeftButtonDown += (_, e) =>
        {
            SelectedDiskNode = node;
            if (e.ClickCount >= 2 && node.Children.Count > 0) { DrillDown(node); e.Handled = true; }
        };
        shell.ContextMenu = BuildTileContextMenu(node);
        shell.MouseEnter += (_, _) =>
        {
            shell.BorderBrush     = new SolidColorBrush(Color.FromRgb(0xE0, 0x60, 0x20));
            shell.BorderThickness = new Thickness(2);
        };
        shell.MouseLeave += (_, _) =>
        {
            shell.BorderBrush     = new SolidColorBrush(Color.FromRgb(0x08, 0x08, 0x08));
            shell.BorderThickness = new Thickness(1);
        };

        Canvas.SetLeft(shell, rect.X);
        Canvas.SetTop(shell,  rect.Y);
        TreeCanvas.Children.Add(shell);

        // Thin name bar at the top of the directory tile
        if (h >= 18)
        {
            var hdr = new TextBlock
            {
                Text         = $"📁 {node.Name}  {FormatSize(node.SizeBytes)}",
                FontSize     = 9,
                Foreground   = new SolidColorBrush(Color.FromRgb(0xA8, 0xA8, 0xA8)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin       = new Thickness(3, 2, 3, 0),
                MaxWidth     = Math.Max(0, w - 6),
                IsHitTestVisible = false, // let clicks pass through to shell
            };
            Canvas.SetLeft(hdr, rect.X + 2);
            Canvas.SetTop(hdr,  rect.Y + 1);
            TreeCanvas.Children.Add(hdr);
        }
    }

    private void RenderTile(DiskNode node, Rect rect, Color baseColor)
    {
        // Reject degenerate rects (NaN, infinity, sub-pixel)
        if (!double.IsFinite(rect.Width)  || !double.IsFinite(rect.Height) ||
            !double.IsFinite(rect.X)      || !double.IsFinite(rect.Y)      ||
            rect.Width < 2 || rect.Height < 2) return;

        bool canDrill = node.IsDirectory && node.Children.Count > 0;

        var border = new Border
        {
            Width               = Math.Max(0, rect.Width  - 1),
            Height              = Math.Max(0, rect.Height - 1),
            Background          = new SolidColorBrush(baseColor),
            BorderBrush         = new SolidColorBrush(Color.FromRgb(0x08, 0x08, 0x08)),
            BorderThickness     = new Thickness(1),
            Cursor              = canDrill ? Cursors.Hand : Cursors.Arrow,
            ClipToBounds        = true,
            SnapsToDevicePixels = true,
        };

        // ── Label ──────────────────────────────────────────────────────────
        double w = rect.Width;
        double h = rect.Height;

        if (w > 36 && h > 18)
        {
            double nameFontSize = Math.Clamp(h * 0.13, 9.0, 13.0);
            double sizeFontSize = Math.Clamp(nameFontSize * 0.85, 8.0, 11.0);

            var stack = new StackPanel { Margin = new Thickness(4, 2, 4, 2) };

            stack.Children.Add(new TextBlock
            {
                Text          = node.Name,
                FontSize      = nameFontSize,
                Foreground    = Brushes.White,
                TextTrimming  = TextTrimming.CharacterEllipsis,
                TextWrapping  = TextWrapping.NoWrap,
            });

            if (h > 32)
            {
                stack.Children.Add(new TextBlock
                {
                    Text       = FormatSize(node.SizeBytes),
                    FontSize   = sizeFontSize,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xC0, 0xD8)),
                });
            }

            border.Child = stack;
        }

        // ── Tooltip ────────────────────────────────────────────────────────
        border.ToolTip = $"{(node.IsDirectory ? "📁" : "📄")} {node.Name}\n" +
                         $"Size: {FormatSize(node.SizeBytes)}\n" +
                         $"Path: {node.FullPath}" +
                         (canDrill ? "\nDouble-click to drill in" : string.Empty);

        // ── Mouse events ───────────────────────────────────────────────────
        border.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount >= 2 && canDrill)
            {
                DrillDown(node);
                e.Handled = true;
            }
            else
            {
                SelectedDiskNode = node;
            }
        };

        border.MouseEnter += (_, _) =>
        {
            border.BorderBrush     = new SolidColorBrush(Color.FromRgb(0xE0, 0x60, 0x20));
            border.BorderThickness = new Thickness(2);
        };

        border.MouseLeave += (_, _) =>
        {
            border.BorderBrush     = new SolidColorBrush(Color.FromRgb(0x08, 0x08, 0x08));
            border.BorderThickness = new Thickness(1);
        };

        border.ContextMenu = BuildTileContextMenu(node);

        Canvas.SetLeft(border, rect.X);
        Canvas.SetTop(border,  rect.Y);
        TreeCanvas.Children.Add(border);
    }

    // ── Tile context menu ──────────────────────────────────────────────────

    private ContextMenu BuildTileContextMenu(DiskNode node)
    {
        var menu = new ContextMenu
        {
            Background      = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11)),
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0x3A, 0x0A, 0x0A)),
            BorderThickness = new Thickness(1),
        };

        menu.Opened += (_, _) => SelectedDiskNode = node;

        MenuItem MakeItem(string header, Action action)
        {
            var mi = new MenuItem
            {
                Header     = header,
                Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)),
                Background = Brushes.Transparent,
            };
            mi.Click += (_, _) => action();
            return mi;
        }

        menu.Items.Add(MakeItem("🗂  Open in Explorer", () =>
        {
            string args = node.IsDirectory
                ? $"\"{node.FullPath}\""
                : $"/select,\"{node.FullPath}\"";
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo("explorer.exe", args)
                { UseShellExecute = true });
        }));

        menu.Items.Add(MakeItem("📋  Copy Path", () =>
            Clipboard.SetText(node.FullPath)));

        if (node.IsDirectory && node.Children.Count > 0)
        {
            menu.Items.Add(new Separator
            {
                Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x0A, 0x0A)),
                Margin     = new Thickness(6, 2, 6, 2),
            });
            menu.Items.Add(MakeItem("🔍  Drill In", () => DrillDown(node)));
        }

        return menu;
    }

    // ── Squarified treemap layout ──────────────────────────────────────────

    /// <summary>Max tiles rendered per level — keeps WPF element count sane.</summary>
    private const int MaxTiles = 500;

    /// <summary>Computes tile rectangles for the direct children of <paramref name="root"/>.</summary>
    private static List<(DiskNode Node, Rect Rect)> ComputeLayout(DiskNode root, Rect bounds)
    {
        var result = new List<(DiskNode, Rect)>();

        // Cap to top-N by size — directories with tens of thousands of files would
        // otherwise produce thousands of invisible sub-pixel tiles and overflow the stack.
        var items = root.Children
            .Where(n => n.SizeBytes > 0)
            .OrderByDescending(n => n.SizeBytes)
            .Take(MaxTiles)
            .ToList();

        if (items.Count == 0) return result;

        double total     = items.Sum(n => (double)n.SizeBytes);
        double totalArea = bounds.Width * bounds.Height;

        var weighted = items
            .Select(n => (Node: n, Area: n.SizeBytes / total * totalArea))
            .ToList();

        Squarify(weighted, bounds, result);
        return result;
    }

    /// <summary>
    /// Iterative squarified layout — avoids stack overflow on directories with
    /// hundreds or thousands of children.
    /// </summary>
    private static void Squarify(
        List<(DiskNode Node, double Area)> items,
        Rect startBounds,
        List<(DiskNode, Rect)> result)
    {
        var remaining = items;
        var bounds    = startBounds;

        while (remaining.Count > 0 && bounds.Width >= 0.5 && bounds.Height >= 0.5)
        {
            if (remaining.Count == 1)
            {
                result.Add((remaining[0].Node, bounds));
                break;
            }

            double w         = Math.Min(bounds.Width, bounds.Height);
            int    rowEnd    = 1;
            double rowSum    = remaining[0].Area;
            double rowMax    = remaining[0].Area;
            double rowMin    = remaining[0].Area;
            double prevWorst = WorstRatio(rowSum, rowMax, rowMin, w);

            for (int i = 1; i < remaining.Count; i++)
            {
                double a        = remaining[i].Area;
                double newSum   = rowSum + a;
                double newMax   = Math.Max(rowMax, a);
                double newMin   = Math.Min(rowMin, a);
                double newWorst = WorstRatio(newSum, newMax, newMin, w);

                if (newWorst > prevWorst) break;

                rowEnd    = i + 1;
                rowSum    = newSum;
                rowMax    = newMax;
                rowMin    = newMin;
                prevWorst = newWorst;
            }

            var row = remaining.Take(rowEnd).ToList();
            remaining = remaining.Skip(rowEnd).ToList();
            bounds    = LayoutRow(row, rowSum, bounds, result);
        }
    }

    /// <summary>Lays out one strip of tiles; returns the leftover rectangle.</summary>
    /// <remarks>
    /// For a landscape rect (W >= H) we place a VERTICAL COLUMN on the left:
    ///   column width = rowSum / H  (always ≤ W because rowSum ≤ totalArea = W*H)
    ///   tiles stack top-to-bottom, each with height = area / columnWidth
    ///   remainder is the rectangle to the right of the column.
    ///
    /// For a portrait rect (H > W) we place a HORIZONTAL BAND on the top:
    ///   band height = rowSum / W
    ///   tiles go left-to-right, each with width = area / bandHeight
    ///   remainder is the rectangle below the band.
    ///
    /// Swapping these (the prior bug) caused thickness = rowSum/H to exceed H
    /// when a single large item occupied most of the landscape canvas, pushing
    /// every subsequent tile below the visible edge of the window.
    /// </remarks>
    private static Rect LayoutRow(
        List<(DiskNode Node, double Area)> row,
        double rowSum,
        Rect bounds,
        List<(DiskNode, Rect)> result)
    {
        bool   landscape  = bounds.Width >= bounds.Height; // landscape → vertical column
        double shortSide  = landscape ? bounds.Height : bounds.Width;
        if (shortSide < 0.5) return bounds;

        double thickness = rowSum / shortSide;   // column-width (landscape) or band-height (portrait)
        if (!double.IsFinite(thickness) || thickness < 0.5) return bounds;

        // pos tracks position along the LONG axis (Y for column, X for band)
        double pos = landscape ? bounds.Y : bounds.X;

        foreach (var (node, area) in row)
        {
            double length = thickness > 0 ? area / thickness : 0;

            Rect rect = landscape
                ? new Rect(bounds.X, pos, Math.Max(0, thickness), Math.Max(0, length))   // column: fixed X, advancing Y
                : new Rect(pos, bounds.Y, Math.Max(0, length),    Math.Max(0, thickness)); // band:   advancing X, fixed Y

            result.Add((node, rect));
            pos += length;
        }

        // Clamp residual to prevent sub-pixel float drift from producing negative dimensions.
        return landscape
            ? new Rect(bounds.X + thickness, bounds.Y, Math.Max(0, bounds.Width  - thickness), bounds.Height)
            : new Rect(bounds.X, bounds.Y + thickness, bounds.Width, Math.Max(0, bounds.Height - thickness));
    }

    /// <summary>
    /// Worst (maximum) aspect ratio for a row with the given sum, max area, min area,
    /// and short-side length <paramref name="w"/>.
    /// </summary>
    private static double WorstRatio(double sum, double maxArea, double minArea, double w)
    {
        if (sum <= 0 || w <= 0) return double.MaxValue;
        double s2 = sum * sum;
        double w2 = w   * w;
        // Derived from: rowThickness = sum/w; length = area/rowThickness = area*w/sum
        // aspect = max(rowThickness/length, length/rowThickness)
        //        = max(sum²/(area*w²), area*w²/sum²)
        // Worst over all items: max-area item maximises the second term,
        //                        min-area item maximises the first term.
        return Math.Max(w2 * maxArea / s2, s2 / (w2 * minArea));
    }

    // ── Navigation ─────────────────────────────────────────────────────────

    private void DrillDown(DiskNode node)
    {
        _navStack.Push(_displayRoot!);
        _displayRoot = node;
        UpdateBreadcrumb();
        Render();
    }

    private void NavigateTo(DiskNode target)
    {
        // Unwind the stack until target is at the top, then pop it
        while (_navStack.Count > 0 && _navStack.Peek() != target)
            _navStack.Pop();

        if (_navStack.Count > 0)
            _navStack.Pop();

        _displayRoot = target;
        UpdateBreadcrumb();
        Render();
    }

    private void UpdateBreadcrumb()
    {
        BreadcrumbPanel.Children.Clear();

        // Build full path: stack items (oldest first) + current display root
        var path = _navStack.Reverse().ToList();
        if (_displayRoot != null) path.Add(_displayRoot);

        for (int i = 0; i < path.Count; i++)
        {
            var node   = path[i];
            bool isLast = i == path.Count - 1;

            var crumb = new Button
            {
                Content         = (i == 0) ? "⌂ Root" : node.Name,
                Background      = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding         = new Thickness(4, 1, 4, 1),
                FontSize        = 12,
                Foreground      = isLast
                    ? new SolidColorBrush(Color.FromRgb(0xE0, 0x60, 0x20))
                    : new SolidColorBrush(Color.FromRgb(0x00, 0xC0, 0xD8)),
                Cursor    = isLast ? Cursors.Arrow : Cursors.Hand,
                IsEnabled = !isLast,
                ToolTip   = node.FullPath,
            };

            if (!isLast)
            {
                var captured = node;
                crumb.Click += (_, _) => NavigateTo(captured);
            }

            BreadcrumbPanel.Children.Add(crumb);

            if (!isLast)
            {
                BreadcrumbPanel.Children.Add(new TextBlock
                {
                    Text                = " › ",
                    Foreground          = new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x50)),
                    VerticalAlignment   = VerticalAlignment.Center,
                    FontSize            = 12,
                });
            }
        }
    }

    // ── Color helpers ──────────────────────────────────────────────────────

    /// <summary>Brighten a color by <paramref name="amount"/> per channel (0-255).</summary>
    private static Color LightenColor(Color c, int amount) => Color.FromRgb(
        (byte)Math.Min(255, c.R + amount),
        (byte)Math.Min(255, c.G + amount),
        (byte)Math.Min(255, c.B + amount));

    /// <summary>Darken a color by <paramref name="amount"/> per channel (0-255).</summary>
    private static Color DarkenColor(Color c, int amount) => Color.FromRgb(
        (byte)Math.Max(0, c.R - amount),
        (byte)Math.Max(0, c.G - amount),
        (byte)Math.Max(0, c.B - amount));

    // ── Helpers ────────────────────────────────────────────────────────────

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
        >= 1_048_576     => $"{bytes / 1_048_576.0:F0} MB",
        _                => $"{bytes / 1024.0:F0} KB",
    };
}
