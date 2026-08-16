using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AIEngineeringWorkspace.Controls;
using AIEngineeringWorkspace.Infrastructure;
using AIEngineeringWorkspace.Workspace;

namespace AIEngineeringWorkspace;

public partial class MainWindow : Window
{
    private const string DefaultBrowserUrl = "https://www.google.com/";
    private const int MaxBrowserPanes = 8;
    private const int MaxFilePanes = 4;
    private const double PaneGap = 8;

    private readonly DispatcherTimer _healthTimer;
    private readonly List<BrowserTile> _browserPanes = new();
    private readonly List<FilePane> _filePanes = new();
    private readonly Dictionary<FrameworkElement, Point> _moveOrigins = new();
    private bool _closing;
    private bool _showEndpointIds;
    private bool _workspaceInitialized;
    private bool _autoFitLayout = true;
    private FrameworkElement? _maximizedPane;
    private Point _maximizedPanePosition;
    private Size _maximizedPaneSize;
    private int _zCounter = 1;

    public MainWindow()
    {
        InitializeComponent();

        _healthTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _healthTimer.Tick += (_, _) =>
        {
            foreach (var tile in _browserPanes.ToArray())
            {
                tile.CheckHealth();
            }
        };

        Loaded += (_, _) =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (!_workspaceInitialized)
                {
                    CreateDefaultWorkspaceToFitViewport();
                    _workspaceInitialized = true;
                }

                RuntimeLog.Info($"MainWindow loaded. BrowserPaneCount={_browserPanes.Count}; FilePaneCount={_filePanes.Count}; DefaultBrowserUrl='{DefaultBrowserUrl}'; Canvas={WorkspaceCanvas.Width:0}x{WorkspaceCanvas.Height:0}; RuntimeLog='{RuntimeLog.CurrentPath}'");
                _healthTimer.Start();
                SetWorkspaceStatus("Ready. Auto Fit fills the Workspace; drag/resize switches to Free Layout. Browser □ maximizes a pane inside the Workspace.");
                UpdatePaneCounts();
            }, DispatcherPriority.Loaded);
        };

        WorkspaceScrollViewer.SizeChanged += (_, _) =>
        {
            if (_workspaceInitialized && _autoFitLayout && _maximizedPane is null)
            {
                Dispatcher.InvokeAsync(ApplyAutoFitLayout, DispatcherPriority.Background);
            }
        };

        Closing += (_, _) => ShutdownWorkspace();
    }

    private void CreateDefaultWorkspaceToFitViewport()
    {
        AddFilePane(GetDefaultFilePath(1));
        AddFilePane(GetDefaultFilePath(2));
        AddBrowserPane();
        AddBrowserPane();
        AddBrowserPane();
        AddBrowserPane();
        ApplyAutoFitLayout();

        RuntimeLog.Info($"Default workspace created and auto-fit. BrowserPanes={_browserPanes.Count}; FilePanes={_filePanes.Count}; Viewport={WorkspaceScrollViewer.ViewportWidth:0}x{WorkspaceScrollViewer.ViewportHeight:0}");
    }

    private static string GetDefaultFilePath(int slot)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var preferred = slot switch
        {
            1 => Path.Combine(userProfile, "Downloads"),
            2 => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            _ => userProfile
        };

        return Directory.Exists(preferred) ? preferred : userProfile;
    }

    private int? AllocateDisplayIndex(PaneKind kind)
    {
        var max = kind == PaneKind.Browser ? MaxBrowserPanes : MaxFilePanes;
        var used = kind == PaneKind.Browser
            ? _browserPanes.Select(p => p.Identity.DisplayIndex).ToHashSet()
            : _filePanes.Select(p => p.Identity.DisplayIndex).ToHashSet();

        for (var index = 1; index <= max; index++)
        {
            if (!used.Contains(index))
            {
                return index;
            }
        }

        return null;
    }

    private void AddBrowserPane(Point? position = null, double width = 620, double height = 420)
    {
        var displayIndex = AllocateDisplayIndex(PaneKind.Browser);
        if (displayIndex is null)
        {
            SetWorkspaceStatus($"Browser pane limit reached ({MaxBrowserPanes}).");
            return;
        }

        var identity = PaneIdentity.Create(PaneKind.Browser, displayIndex.Value);
        var tile = new BrowserTile();
        tile.Configure(identity, DefaultBrowserUrl);
        tile.SetEndpointIdVisibility(_showEndpointIds);
        tile.StatusChanged += BrowserPane_StatusChanged;
        tile.ClosePaneRequested += BrowserPane_ClosePaneRequested;
        tile.MoveStarted += BrowserPane_MoveStarted;
        tile.MoveRequested += BrowserPane_MoveRequested;
        tile.MoveCompleted += BrowserPane_MoveCompleted;
        tile.ResizeRequested += BrowserPane_ResizeRequested;
        tile.ActivateRequested += BrowserPane_ActivateRequested;
        tile.MaximizeRequested += BrowserPane_MaximizeRequested;
        tile.Width = width;
        tile.Height = height;

        _browserPanes.Add(tile);
        WorkspaceCanvas.Children.Add(tile);
        SetPanePosition(tile, position ?? FindFreePosition(width, height));
        BringToFront(tile);
        EnsureCanvasBounds(tile);
        UpdatePaneCounts();
        if (_workspaceInitialized && _autoFitLayout && _maximizedPane is null)
        {
            ApplyAutoFitLayout();
        }

        RuntimeLog.Info($"[{identity.Alias}] Browser pane added. PaneId={identity.PaneId:D}; DisplayIndex={identity.DisplayIndex}; Position={FormatPoint(GetPanePosition(tile))}; Size={tile.Width:0}x{tile.Height:0}; Count={_browserPanes.Count}");
    }

    private void AddFilePane(string? initialPath = null, Point? position = null, double width = 380, double height = 340)
    {
        var displayIndex = AllocateDisplayIndex(PaneKind.File);
        if (displayIndex is null)
        {
            SetWorkspaceStatus($"File pane limit reached ({MaxFilePanes}).");
            return;
        }

        var identity = PaneIdentity.Create(PaneKind.File, displayIndex.Value);
        var pane = new FilePane();
        pane.Configure(identity, initialPath ?? GetDefaultFilePath(displayIndex.Value));
        pane.SetEndpointIdVisibility(_showEndpointIds);
        pane.StatusChanged += FilePane_StatusChanged;
        pane.ClosePaneRequested += FilePane_ClosePaneRequested;
        pane.MoveStarted += FilePane_MoveStarted;
        pane.MoveRequested += FilePane_MoveRequested;
        pane.MoveCompleted += FilePane_MoveCompleted;
        pane.ResizeRequested += FilePane_ResizeRequested;
        pane.ActivateRequested += FilePane_ActivateRequested;
        pane.Width = width;
        pane.Height = height;

        _filePanes.Add(pane);
        WorkspaceCanvas.Children.Add(pane);
        SetPanePosition(pane, position ?? FindFreePosition(width, height));
        BringToFront(pane);
        EnsureCanvasBounds(pane);
        UpdatePaneCounts();
        if (_workspaceInitialized && _autoFitLayout && _maximizedPane is null)
        {
            ApplyAutoFitLayout();
        }

        RuntimeLog.Info($"[{identity.Alias}] File pane added. PaneId={identity.PaneId:D}; DisplayIndex={identity.DisplayIndex}; Position={FormatPoint(GetPanePosition(pane))}; Size={pane.Width:0}x{pane.Height:0}; Count={_filePanes.Count}");
    }

    private void BrowserPane_ClosePaneRequested(BrowserTile tile)
    {
        if (_closing || !_browserPanes.Contains(tile))
        {
            return;
        }

        var alias = tile.Identity.Alias;
        var paneId = tile.Identity.PaneId;
        RuntimeLog.Info($"[{alias}] Pane close requested by user. PaneId={paneId:D}");
        if (ReferenceEquals(_maximizedPane, tile))
        {
            ClearMaximizedPaneState(tile);
        }
        tile.Shutdown();
        UnsubscribeBrowserPane(tile);
        WorkspaceCanvas.Children.Remove(tile);
        _browserPanes.Remove(tile);
        _moveOrigins.Remove(tile);
        UpdatePaneCounts();
        if (_autoFitLayout && _maximizedPane is null)
        {
            ApplyAutoFitLayout();
        }
        SetWorkspaceStatus($"Closed {alias}. Its display index is now available for reuse.");
    }

    private void FilePane_ClosePaneRequested(FilePane pane)
    {
        if (_closing || !_filePanes.Contains(pane))
        {
            return;
        }

        var alias = pane.Identity.Alias;
        var paneId = pane.Identity.PaneId;
        RuntimeLog.Info($"[{alias}] Pane close requested by user. PaneId={paneId:D}");
        UnsubscribeFilePane(pane);
        WorkspaceCanvas.Children.Remove(pane);
        _filePanes.Remove(pane);
        _moveOrigins.Remove(pane);
        UpdatePaneCounts();
        if (_autoFitLayout && _maximizedPane is null)
        {
            ApplyAutoFitLayout();
        }
        SetWorkspaceStatus($"Closed {alias}. Its display index is now available for reuse.");
    }

    private void UnsubscribeBrowserPane(BrowserTile tile)
    {
        tile.StatusChanged -= BrowserPane_StatusChanged;
        tile.ClosePaneRequested -= BrowserPane_ClosePaneRequested;
        tile.MoveStarted -= BrowserPane_MoveStarted;
        tile.MoveRequested -= BrowserPane_MoveRequested;
        tile.MoveCompleted -= BrowserPane_MoveCompleted;
        tile.ResizeRequested -= BrowserPane_ResizeRequested;
        tile.ActivateRequested -= BrowserPane_ActivateRequested;
        tile.MaximizeRequested -= BrowserPane_MaximizeRequested;
    }

    private void UnsubscribeFilePane(FilePane pane)
    {
        pane.StatusChanged -= FilePane_StatusChanged;
        pane.ClosePaneRequested -= FilePane_ClosePaneRequested;
        pane.MoveStarted -= FilePane_MoveStarted;
        pane.MoveRequested -= FilePane_MoveRequested;
        pane.MoveCompleted -= FilePane_MoveCompleted;
        pane.ResizeRequested -= FilePane_ResizeRequested;
        pane.ActivateRequested -= FilePane_ActivateRequested;
    }

    private void BrowserPane_StatusChanged(BrowserTile tile, string message)
        => SetWorkspaceStatus($"{tile.Identity.Alias}: {message}");

    private void FilePane_StatusChanged(FilePane pane, string message)
        => SetWorkspaceStatus($"{pane.Identity.Alias}: {message}");

    private void BrowserPane_MoveStarted(BrowserTile tile) => BeginPaneMove(tile);
    private void FilePane_MoveStarted(FilePane pane) => BeginPaneMove(pane);

    private void BrowserPane_MoveRequested(BrowserTile tile, double dx, double dy) => MovePane(tile, dx, dy);
    private void FilePane_MoveRequested(FilePane pane, double dx, double dy) => MovePane(pane, dx, dy);

    private void BrowserPane_MoveCompleted(BrowserTile tile) => CompletePaneMove(tile);
    private void FilePane_MoveCompleted(FilePane pane) => CompletePaneMove(pane);

    private void BrowserPane_ResizeRequested(BrowserTile tile, PaneResizeDirection direction, double dx, double dy) => ResizePane(tile, direction, dx, dy);
    private void FilePane_ResizeRequested(FilePane pane, PaneResizeDirection direction, double dx, double dy) => ResizePane(pane, direction, dx, dy);

    private void BrowserPane_ActivateRequested(BrowserTile tile) => BringToFront(tile);
    private void FilePane_ActivateRequested(FilePane pane) => BringToFront(pane);
    private void BrowserPane_MaximizeRequested(BrowserTile tile) => ToggleBrowserPaneMaximize(tile);

    private void BeginPaneMove(FrameworkElement pane)
    {
        if (_autoFitLayout)
        {
            SetLayoutMode(false, "Manual pane move");
        }

        _moveOrigins[pane] = GetPanePosition(pane);
        BringToFront(pane);
        RuntimeLog.Info($"[{GetAlias(pane)}] Pane move started. PaneId={GetPaneId(pane):D}; Origin={FormatPoint(_moveOrigins[pane])}");
    }

    private void MovePane(FrameworkElement pane, double dx, double dy)
    {
        var position = GetPanePosition(pane);
        var newPosition = new Point(
            Math.Max(0, position.X + dx),
            Math.Max(0, position.Y + dy));
        SetPanePosition(pane, newPosition);
        EnsureCanvasBounds(pane);
    }

    private void CompletePaneMove(FrameworkElement pane)
    {
        var alias = GetAlias(pane);
        var origin = _moveOrigins.TryGetValue(pane, out var saved) ? saved : GetPanePosition(pane);
        _moveOrigins.Remove(pane);

        var paneRect = GetPaneRect(pane);
        var swapTarget = GetAllPanes()
            .Where(other => !ReferenceEquals(other, pane))
            .Select(other => new { Pane = other, Area = IntersectionArea(paneRect, GetPaneRect(other)) })
            .Where(x => x.Area > 0)
            .OrderByDescending(x => x.Area)
            .FirstOrDefault();

        if (swapTarget is not null)
        {
            var target = swapTarget.Pane;
            var targetPosition = GetPanePosition(target);
            SetPanePosition(pane, targetPosition);
            SetPanePosition(target, origin);
            EnsureCanvasBounds(pane);
            EnsureCanvasBounds(target);

            RuntimeLog.Info($"[{alias}] Pane move completed as position swap with {GetAlias(target)}. PaneId={GetPaneId(pane):D}; NewPosition={FormatPoint(targetPosition)}; OtherPaneId={GetPaneId(target):D}; OtherNewPosition={FormatPoint(origin)}");
            SetWorkspaceStatus($"Swapped {alias} with {GetAlias(target)}. Pane identity did not change.");
        }
        else
        {
            var final = GetPanePosition(pane);
            RuntimeLog.Info($"[{alias}] Pane move completed. PaneId={GetPaneId(pane):D}; Position={FormatPoint(final)}");
            SetWorkspaceStatus($"Moved {alias} to {FormatPoint(final)}.");
        }
    }

    private void ResizePane(FrameworkElement pane, PaneResizeDirection direction, double dx, double dy)
    {
        if (_autoFitLayout)
        {
            SetLayoutMode(false, "Manual pane border resize");
        }

        var width = double.IsNaN(pane.Width) ? pane.ActualWidth : pane.Width;
        var height = double.IsNaN(pane.Height) ? pane.ActualHeight : pane.Height;
        var position = GetPanePosition(pane);
        var left = position.X;
        var top = position.Y;

        var resizeLeft = direction is PaneResizeDirection.Left or PaneResizeDirection.TopLeft or PaneResizeDirection.BottomLeft;
        var resizeRight = direction is PaneResizeDirection.Right or PaneResizeDirection.TopRight or PaneResizeDirection.BottomRight;
        var resizeTop = direction is PaneResizeDirection.Top or PaneResizeDirection.TopLeft or PaneResizeDirection.TopRight;
        var resizeBottom = direction is PaneResizeDirection.Bottom or PaneResizeDirection.BottomLeft or PaneResizeDirection.BottomRight;

        if (resizeLeft)
        {
            var effectiveDx = dx;
            if (width - effectiveDx < pane.MinWidth)
            {
                effectiveDx = width - pane.MinWidth;
            }

            if (left + effectiveDx < 0)
            {
                effectiveDx = -left;
            }

            left += effectiveDx;
            width -= effectiveDx;
        }
        else if (resizeRight)
        {
            width = Math.Max(pane.MinWidth, width + dx);
        }

        if (resizeTop)
        {
            var effectiveDy = dy;
            if (height - effectiveDy < pane.MinHeight)
            {
                effectiveDy = height - pane.MinHeight;
            }

            if (top + effectiveDy < 0)
            {
                effectiveDy = -top;
            }

            top += effectiveDy;
            height -= effectiveDy;
        }
        else if (resizeBottom)
        {
            height = Math.Max(pane.MinHeight, height + dy);
        }

        pane.Width = width;
        pane.Height = height;
        SetPanePosition(pane, new Point(left, top));
        EnsureCanvasBounds(pane);

        if (pane is BrowserTile browser)
        {
            browser.FitBrowserToPane();
        }

        SetWorkspaceStatus($"Resized {GetAlias(pane)} from {direction} to {width:0}×{height:0} at ({left:0},{top:0}).");
    }

    private void SetLayoutMode(bool autoFit, string reason)
    {
        if (_autoFitLayout == autoFit)
        {
            if (autoFit)
            {
                ApplyAutoFitLayout();
            }
            return;
        }

        RestoreMaximizedPaneIfNeeded();
        _autoFitLayout = autoFit;
        LayoutModeGlyph.Text = autoFit ? "▦" : "✥";
        LayoutModeButton.ToolTip = autoFit
            ? "Auto Fit layout is ON. Panes automatically fill the available Workspace. Click for Free Layout."
            : "Free Layout is ON. Panes keep manual positions/sizes. Click for Auto Fit.";

        if (autoFit)
        {
            ApplyAutoFitLayout();
        }

        RuntimeLog.Info($"Workspace layout mode changed. Mode={(autoFit ? "AutoFit" : "Free")}; Reason='{reason}'");
        SetWorkspaceStatus(autoFit
            ? "Auto Fit enabled. Panes fill the available Workspace and reflow after add/remove."
            : "Free Layout enabled. Drag and resize panes without automatic reflow.");
    }

    private void ApplyAutoFitLayout()
    {
        if (!_autoFitLayout || _maximizedPane is not null)
        {
            return;
        }

        var panes = GetAllPanes()
            .OrderBy(p => GetPanePosition(p).Y)
            .ThenBy(p => GetPanePosition(p).X)
            .ThenBy(GetAlias, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (panes.Count == 0)
        {
            return;
        }

        var viewportWidth = WorkspaceScrollViewer.ViewportWidth;
        var viewportHeight = WorkspaceScrollViewer.ViewportHeight;
        if (viewportWidth <= 1)
        {
            viewportWidth = Math.Max(900, WorkspaceScrollViewer.ActualWidth - 8);
        }
        if (viewportHeight <= 1)
        {
            viewportHeight = Math.Max(560, WorkspaceScrollViewer.ActualHeight - 8);
        }

        var surfaceWidth = Math.Max(900, viewportWidth - 4);
        var surfaceHeight = Math.Max(560, viewportHeight - 4);
        WorkspaceCanvas.Width = surfaceWidth;
        WorkspaceCanvas.Height = surfaceHeight;

        var columns = panes.Count switch
        {
            1 => 1,
            2 => 2,
            3 => 3,
            4 => 2,
            <= 6 => 3,
            <= 8 => 4,
            9 => 3,
            _ => 4
        };
        var rows = (int)Math.Ceiling(panes.Count / (double)columns);
        var availableHeight = surfaceHeight - (PaneGap * 2) - (PaneGap * Math.Max(0, rows - 1));
        var rowHeight = Math.Max(1, availableHeight / rows);

        var index = 0;
        for (var row = 0; row < rows; row++)
        {
            var remaining = panes.Count - index;
            var itemsInRow = Math.Min(columns, remaining);
            var availableWidth = surfaceWidth - (PaneGap * 2) - (PaneGap * Math.Max(0, itemsInRow - 1));
            var cellWidth = Math.Max(1, availableWidth / itemsInRow);
            var y = PaneGap + (row * (rowHeight + PaneGap));

            for (var column = 0; column < itemsInRow; column++)
            {
                var pane = panes[index++];
                var x = PaneGap + (column * (cellWidth + PaneGap));
                var width = Math.Max(pane.MinWidth, cellWidth);
                var height = Math.Max(pane.MinHeight, rowHeight);

                pane.Visibility = Visibility.Visible;
                pane.Width = width;
                pane.Height = height;
                SetPanePosition(pane, new Point(x, y));

                if (pane is BrowserTile browser)
                {
                    browser.FitBrowserToPane();
                }
            }
        }

        RuntimeLog.Info($"Auto Fit applied. Panes={panes.Count}; Grid={columns}x{rows}; Surface={surfaceWidth:0}x{surfaceHeight:0}");
    }

    private void ToggleBrowserPaneMaximize(BrowserTile tile)
    {
        if (!_browserPanes.Contains(tile))
        {
            return;
        }

        if (ReferenceEquals(_maximizedPane, tile))
        {
            RestoreMaximizedPaneIfNeeded();
            return;
        }

        RestoreMaximizedPaneIfNeeded();
        _maximizedPane = tile;
        _maximizedPanePosition = GetPanePosition(tile);
        _maximizedPaneSize = new Size(
            double.IsNaN(tile.Width) ? tile.ActualWidth : tile.Width,
            double.IsNaN(tile.Height) ? tile.ActualHeight : tile.Height);

        foreach (var pane in GetAllPanes())
        {
            pane.Visibility = ReferenceEquals(pane, tile) ? Visibility.Visible : Visibility.Collapsed;
        }

        var viewportWidth = Math.Max(900, WorkspaceScrollViewer.ViewportWidth > 1 ? WorkspaceScrollViewer.ViewportWidth - 4 : WorkspaceScrollViewer.ActualWidth - 8);
        var viewportHeight = Math.Max(560, WorkspaceScrollViewer.ViewportHeight > 1 ? WorkspaceScrollViewer.ViewportHeight - 4 : WorkspaceScrollViewer.ActualHeight - 8);
        WorkspaceCanvas.Width = viewportWidth;
        WorkspaceCanvas.Height = viewportHeight;
        SetPanePosition(tile, new Point(PaneGap, PaneGap));
        tile.Width = Math.Max(tile.MinWidth, viewportWidth - (PaneGap * 2));
        tile.Height = Math.Max(tile.MinHeight, viewportHeight - (PaneGap * 2));
        tile.SetMaximizedState(true);
        tile.FitBrowserToPane();
        BringToFront(tile);
        WorkspaceScrollViewer.ScrollToHorizontalOffset(0);
        WorkspaceScrollViewer.ScrollToVerticalOffset(0);

        RuntimeLog.Info($"[{tile.Identity.Alias}] Browser pane maximized inside Workspace. PaneId={tile.Identity.PaneId:D}; Size={tile.Width:0}x{tile.Height:0}");
        SetWorkspaceStatus($"{tile.Identity.Alias} maximized inside the Workspace. Use ❐ to restore the pane layout.");
    }

    private void RestoreMaximizedPaneIfNeeded()
    {
        if (_maximizedPane is null)
        {
            return;
        }

        var pane = _maximizedPane;
        _maximizedPane = null;

        foreach (var item in GetAllPanes())
        {
            item.Visibility = Visibility.Visible;
        }

        if (pane is BrowserTile browser)
        {
            browser.SetMaximizedState(false);
        }

        if (_autoFitLayout)
        {
            ApplyAutoFitLayout();
        }
        else if (GetAllPanes().Contains(pane))
        {
            SetPanePosition(pane, _maximizedPanePosition);
            pane.Width = Math.Max(pane.MinWidth, _maximizedPaneSize.Width);
            pane.Height = Math.Max(pane.MinHeight, _maximizedPaneSize.Height);
            EnsureCanvasBounds(pane);
            if (pane is BrowserTile restoredBrowser)
            {
                restoredBrowser.FitBrowserToPane();
            }
        }

        RuntimeLog.Info($"[{GetAlias(pane)}] Browser pane restored from Workspace maximize.");
        SetWorkspaceStatus($"Restored {GetAlias(pane)} to the Workspace layout.");
    }

    private void ClearMaximizedPaneState(BrowserTile tile)
    {
        if (!ReferenceEquals(_maximizedPane, tile))
        {
            return;
        }

        _maximizedPane = null;
        tile.SetMaximizedState(false);
        foreach (var pane in GetAllPanes())
        {
            pane.Visibility = Visibility.Visible;
        }
    }

    private Point FindFreePosition(double width, double height)
    {
        const double step = 36;
        var surfaceWidth = Math.Max(WorkspaceCanvas.Width, 1200);
        var surfaceHeight = Math.Max(WorkspaceCanvas.Height, 700);

        for (var y = PaneGap; y <= surfaceHeight; y += step)
        {
            for (var x = PaneGap; x <= surfaceWidth; x += step)
            {
                var candidate = new Rect(x, y, width, height);
                if (GetAllPanes().All(p => !InflateRect(GetPaneRect(p), PaneGap).IntersectsWith(candidate)))
                {
                    return new Point(x, y);
                }
            }
        }

        var fallback = new Point(PaneGap, surfaceHeight + PaneGap);
        WorkspaceCanvas.Height = surfaceHeight + height + (PaneGap * 2);
        return fallback;
    }

    private static Rect InflateRect(Rect source, double amount)
    {
        source.Inflate(amount, amount);
        return source;
    }

    private IEnumerable<FrameworkElement> GetAllPanes()
        => _filePanes.Cast<FrameworkElement>().Concat(_browserPanes);

    private static string GetAlias(FrameworkElement pane)
        => pane switch
        {
            BrowserTile browser => browser.Identity.Alias,
            FilePane file => file.Identity.Alias,
            _ => "?"
        };

    private static Guid GetPaneId(FrameworkElement pane)
        => pane switch
        {
            BrowserTile browser => browser.Identity.PaneId,
            FilePane file => file.Identity.PaneId,
            _ => Guid.Empty
        };

    private static Point GetPanePosition(FrameworkElement pane)
    {
        var left = Canvas.GetLeft(pane);
        var top = Canvas.GetTop(pane);
        return new Point(double.IsNaN(left) ? 0 : left, double.IsNaN(top) ? 0 : top);
    }

    private static void SetPanePosition(FrameworkElement pane, Point position)
    {
        Canvas.SetLeft(pane, position.X);
        Canvas.SetTop(pane, position.Y);
    }

    private static Rect GetPaneRect(FrameworkElement pane)
    {
        var position = GetPanePosition(pane);
        var width = double.IsNaN(pane.Width) ? pane.ActualWidth : pane.Width;
        var height = double.IsNaN(pane.Height) ? pane.ActualHeight : pane.Height;
        return new Rect(position.X, position.Y, Math.Max(pane.MinWidth, width), Math.Max(pane.MinHeight, height));
    }

    private static double IntersectionArea(Rect a, Rect b)
    {
        var intersection = Rect.Intersect(a, b);
        return intersection.IsEmpty ? 0 : intersection.Width * intersection.Height;
    }

    private void BringToFront(FrameworkElement pane)
    {
        Panel.SetZIndex(pane, ++_zCounter);
    }

    private void EnsureCanvasBounds(FrameworkElement pane)
    {
        var rect = GetPaneRect(pane);
        var requiredWidth = rect.Right + PaneGap;
        var requiredHeight = rect.Bottom + PaneGap;

        if (requiredWidth > WorkspaceCanvas.Width)
        {
            WorkspaceCanvas.Width = requiredWidth;
        }

        if (requiredHeight > WorkspaceCanvas.Height)
        {
            WorkspaceCanvas.Height = requiredHeight;
        }
    }

    private static string FormatPoint(Point point) => $"({point.X:0},{point.Y:0})";

    private void AddFilePaneButton_Click(object sender, RoutedEventArgs e)
    {
        RestoreMaximizedPaneIfNeeded();
        AddFilePane();
    }

    private void AddBrowserPaneButton_Click(object sender, RoutedEventArgs e)
    {
        RestoreMaximizedPaneIfNeeded();
        AddBrowserPane();
    }

    private void LayoutModeButton_Click(object sender, RoutedEventArgs e)
        => SetLayoutMode(!_autoFitLayout, "Toolbar toggle");

    private void ShowIdsButton_Click(object sender, RoutedEventArgs e)
    {
        _showEndpointIds = !_showEndpointIds;
        foreach (var pane in _browserPanes)
        {
            pane.SetEndpointIdVisibility(_showEndpointIds);
        }

        foreach (var pane in _filePanes)
        {
            pane.SetEndpointIdVisibility(_showEndpointIds);
        }

        ShowIdsButton.ToolTip = _showEndpointIds
            ? "Hide routing endpoint IDs (B1-B8 / F1-F4)"
            : "Show routing endpoint IDs (B1-B8 / F1-F4)";
        RuntimeLog.Info($"Endpoint ID visibility changed. Visible={_showEndpointIds}; BrowserAliases={string.Join(",", _browserPanes.Select(p => p.Identity.Alias))}; FileAliases={string.Join(",", _filePanes.Select(p => p.Identity.Alias))}");
        SetWorkspaceStatus(_showEndpointIds
            ? "Routing endpoint aliases are visible. B1-B8 = browser; F1-F4 = file."
            : "Routing endpoint aliases hidden.");
    }

    private void DetachAllButton_Click(object sender, RoutedEventArgs e)
    {
        var dockedCount = _browserPanes.Count(tile => tile.HasDockedWindow);
        foreach (var tile in _browserPanes.ToArray())
        {
            if (tile.HasDockedWindow)
            {
                tile.Detach();
            }
        }

        SetWorkspaceStatus(dockedCount == 0
            ? "Detach All: no Firefox windows were docked."
            : $"Detach All completed. Restored {dockedCount} Firefox window(s).");
        RuntimeLog.Info($"Detach All completed. DockedCountBefore={dockedCount}");
    }

    private void UpdatePaneCounts()
    {
        var browserAliases = string.Join(",", _browserPanes.OrderBy(p => p.Identity.DisplayIndex).Select(p => p.Identity.Alias));
        var fileAliases = string.Join(",", _filePanes.OrderBy(p => p.Identity.DisplayIndex).Select(p => p.Identity.Alias));
        var layoutText = _autoFitLayout ? "Auto Fit" : "Free Layout";
        PaneCountTextBlock.Text = $"Standard user / API-free / {layoutText} / {_browserPanes.Count} browser [{browserAliases}] + {_filePanes.Count} file [{fileAliases}]";
    }

    private void SetWorkspaceStatus(string message)
    {
        WorkspaceStatusTextBlock.Text = message;
        WorkspaceStatusTextBlock.ToolTip = message;
    }

    private void ShutdownWorkspace()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        _healthTimer.Stop();

        var dockedCount = _browserPanes.Count(tile => tile.HasDockedWindow);
        var workspaceLaunchedCount = _browserPanes.Sum(tile => tile.WorkspaceLaunchedWindowCount);
        RuntimeLog.Info($"MainWindow closing. BrowserPanes={_browserPanes.Count}; FilePanes={_filePanes.Count}; DockedWindows={dockedCount}; WorkspaceLaunchedWindowsToClose={workspaceLaunchedCount}.");

        foreach (var tile in _browserPanes.ToArray())
        {
            tile.Shutdown();
        }
    }
}
