using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
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
    private string? _currentWorkspacePath;
    private bool _workspaceDirty;
    private bool _suppressDirtyTracking;
    private int _newWorkspaceResetCount;

    public MainWindow()
    {
        InitializeComponent();
        Title = $"AI Engineering Workspace — {AppInfo.DisplayVersion}";
        VersionTextBlock.Text = $"{AppInfo.DisplayVersion} — Workspace Project UX + Endpoint Badge Fix";

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
                    SetWorkspaceDirty(false);
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

        Closing += MainWindow_Closing;
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

    private BrowserTile? AddBrowserPane(Point? position = null, double width = 620, double height = 420, PaneIdentity? identityOverride = null)
    {
        var displayIndex = identityOverride?.DisplayIndex ?? AllocateDisplayIndex(PaneKind.Browser);
        if (displayIndex is null)
        {
            SetWorkspaceStatus($"Browser pane limit reached ({MaxBrowserPanes}).");
            return null;
        }

        var identity = identityOverride ?? PaneIdentity.Create(PaneKind.Browser, displayIndex.Value);
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
        return tile;
    }

    private FilePane? AddFilePane(string? initialPath = null, Point? position = null, double width = 380, double height = 340, PaneIdentity? identityOverride = null)
    {
        var displayIndex = identityOverride?.DisplayIndex ?? AllocateDisplayIndex(PaneKind.File);
        if (displayIndex is null)
        {
            SetWorkspaceStatus($"File pane limit reached ({MaxFilePanes}).");
            return null;
        }

        var identity = identityOverride ?? PaneIdentity.Create(PaneKind.File, displayIndex.Value);
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
        pane.PathChanged += FilePane_PathChanged;
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
        return pane;
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
        MarkWorkspaceDirty($"Closed {alias}");
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
        MarkWorkspaceDirty($"Closed {alias}");
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
        pane.PathChanged -= FilePane_PathChanged;
    }

    private void BrowserPane_StatusChanged(BrowserTile tile, string message)
        => SetWorkspaceStatus($"{tile.Identity.Alias}: {message}");

    private void FilePane_StatusChanged(FilePane pane, string message)
        => SetWorkspaceStatus($"{pane.Identity.Alias}: {message}");

    private void FilePane_PathChanged(FilePane pane, string path)
    {
        MarkWorkspaceDirty($"{pane.Identity.Alias} path changed");
    }

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

        MarkWorkspaceDirty($"Moved {alias}");
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

        MarkWorkspaceDirty($"Resized {GetAlias(pane)}");
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

        MarkWorkspaceDirty($"Layout mode changed to {(autoFit ? "Auto Fit" : "Free Layout")}");
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
            .OrderBy(p => p is FilePane ? 0 : 1)
            .ThenBy(p => p switch
            {
                FilePane file => file.Identity.DisplayIndex,
                BrowserTile browser => browser.Identity.DisplayIndex,
                _ => int.MaxValue
            })
            .ToList();

        var viewportWidth = WorkspaceScrollViewer.ViewportWidth > 1
            ? WorkspaceScrollViewer.ViewportWidth - 2
            : Math.Max(1, WorkspaceScrollViewer.ActualWidth - 2);
        var viewportHeight = WorkspaceScrollViewer.ViewportHeight > 1
            ? WorkspaceScrollViewer.ViewportHeight - 2
            : Math.Max(1, WorkspaceScrollViewer.ActualHeight - 2);

        var specs = panes
            .Select(p => new AutoFitPaneSpec(Math.Max(1, p.MinWidth), Math.Max(1, p.MinHeight)))
            .ToArray();
        var plan = AutoFitLayoutPlanner.Plan(viewportWidth, viewportHeight, specs, PaneGap, maxColumns: 4);

        WorkspaceCanvas.Width = plan.CanvasWidth;
        WorkspaceCanvas.Height = plan.CanvasHeight;

        for (var index = 0; index < panes.Count; index++)
        {
            var pane = panes[index];
            var cell = plan.Cells[index];
            pane.Visibility = Visibility.Visible;
            pane.Width = cell.Width;
            pane.Height = cell.Height;
            SetPanePosition(pane, new Point(cell.X, cell.Y));

            if (pane is BrowserTile browser)
            {
                browser.FitBrowserToPane();
            }
        }

        WorkspaceScrollViewer.ScrollToHorizontalOffset(0);
        WorkspaceScrollViewer.ScrollToVerticalOffset(0);

        Dispatcher.InvokeAsync(() =>
        {
            WorkspaceScrollViewer.ScrollToHorizontalOffset(0);
            WorkspaceScrollViewer.ScrollToVerticalOffset(0);
            foreach (var browser in _browserPanes.Where(item => item.Visibility == Visibility.Visible))
            {
                browser.FitBrowserToPane();
                browser.FinalizeBrowserRepaint();
            }
        }, DispatcherPriority.Render);

        RuntimeLog.Info($"Auto Fit applied. Panes={panes.Count}; Grid={plan.Columns}x{plan.Rows}; Viewport={viewportWidth:0}x{viewportHeight:0}; Canvas={plan.CanvasWidth:0}x{plan.CanvasHeight:0}; ScrollRequired={plan.RequiresScrolling}; ScrollReset=0,0");
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
        if (AddFilePane() is not null)
        {
            MarkWorkspaceDirty("File pane added");
        }
    }

    private void AddBrowserPaneButton_Click(object sender, RoutedEventArgs e)
    {
        RestoreMaximizedPaneIfNeeded();
        if (AddBrowserPane() is not null)
        {
            MarkWorkspaceDirty("Browser pane added");
        }
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
            ? "Hide 64×64 routing endpoint badges (B1-B8 / F1-F4)"
            : "Show 64×64 routing endpoint badges (B1-B8 / F1-F4)";
        MarkWorkspaceDirty("Endpoint ID display changed");
        RuntimeLog.Info($"Endpoint ID visibility changed. Visible={_showEndpointIds}; BrowserAliases={string.Join(",", _browserPanes.Select(p => p.Identity.Alias))}; FileAliases={string.Join(",", _filePanes.Select(p => p.Identity.Alias))}");
        SetWorkspaceStatus(_showEndpointIds
            ? "Large 64×64 routing endpoint badges are visible. B1-B8 = browser; F1-F4 = file."
            : "Large routing endpoint badges hidden; compact endpoint boxes remain visible.");
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

    private void NewWorkspaceButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmReplaceWorkspace())
        {
            SetWorkspaceStatus("New Workspace canceled; the current Workspace was left unchanged.");
            return;
        }

        ResetWorkspaceToDefaults();
    }

    private void ResetWorkspaceToDefaults()
    {
        try
        {
            _suppressDirtyTracking = true;
            _newWorkspaceResetCount++;
            ClearWorkspacePanes();
            _zCounter = 1;
            _currentWorkspacePath = null;
            _showEndpointIds = false;
            _autoFitLayout = true;
            _workspaceInitialized = false;
            CreateDefaultWorkspaceToFitViewport();
            _workspaceInitialized = true;
            LayoutModeGlyph.Text = "▦";
            LayoutModeButton.ToolTip = "Auto Fit layout is ON. Panes automatically fill the available Workspace. Click for Free Layout.";
            ShowIdsButton.ToolTip = "Show 64×64 routing endpoint badges (B1-B8 / F1-F4)";
            SetWorkspaceDirty(false);

            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var status = $"New Workspace project created at {timestamp} (reset #{_newWorkspaceResetCount}). Browser panes launch Google; File paths use the standard defaults.";
            SetWorkspaceStatus(status);
            RuntimeLog.Info($"New Workspace reset completed. Sequence={_newWorkspaceResetCount}; BrowserPanes={_browserPanes.Count}; FilePanes={_filePanes.Count}; AutoFit={_autoFitLayout}; ShowIds={_showEndpointIds}");

            Dispatcher.InvokeAsync(() =>
            {
                if (_autoFitLayout && _maximizedPane is null)
                {
                    ApplyAutoFitLayout();
                }
                WorkspaceScrollViewer.ScrollToHorizontalOffset(0);
                WorkspaceScrollViewer.ScrollToVerticalOffset(0);
            }, DispatcherPriority.Loaded);
        }
        finally
        {
            _suppressDirtyTracking = false;
        }
    }

    private void OpenWorkspaceButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open AI Engineering Workspace",
            Filter = "AI Engineering Workspace (*.aew)|*.aew|JSON files (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = WorkspaceProjectService.DefaultExtension,
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true || !ConfirmReplaceWorkspace())
        {
            return;
        }

        try
        {
            var project = WorkspaceProjectService.Load(dialog.FileName);
            ValidateWorkspaceProject(project);
            ApplyWorkspaceProject(project, dialog.FileName);
        }
        catch (Exception ex)
        {
            RuntimeLog.Error($"Workspace project load failed. Path='{dialog.FileName}'", ex);
            MessageBox.Show(this, $"Unable to open Workspace project.\n\n{ex.Message}", "Open Workspace", MessageBoxButton.OK, MessageBoxImage.Error);
            SetWorkspaceStatus($"Open Workspace failed: {ex.Message}");
        }
    }

    private void SaveWorkspaceButton_Click(object sender, RoutedEventArgs e)
        => SaveCurrentWorkspace();

    private void SaveWorkspaceAsButton_Click(object sender, RoutedEventArgs e)
        => SaveCurrentWorkspaceAs();

    private bool SaveCurrentWorkspace()
    {
        if (string.IsNullOrWhiteSpace(_currentWorkspacePath))
        {
            return SaveCurrentWorkspaceAs();
        }

        return SaveWorkspaceToPath(_currentWorkspacePath);
    }

    private bool SaveCurrentWorkspaceAs()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save AI Engineering Workspace",
            Filter = "AI Engineering Workspace (*.aew)|*.aew|JSON files (*.json)|*.json",
            DefaultExt = WorkspaceProjectService.DefaultExtension,
            AddExtension = true,
            OverwritePrompt = true,
            FileName = string.IsNullOrWhiteSpace(_currentWorkspacePath)
                ? "AI-Engineering-Workspace.aew"
                : Path.GetFileName(_currentWorkspacePath)
        };

        if (dialog.ShowDialog(this) != true)
        {
            return false;
        }

        return SaveWorkspaceToPath(dialog.FileName);
    }

    private bool SaveWorkspaceToPath(string path)
    {
        try
        {
            var project = CaptureWorkspaceProject();
            WorkspaceProjectService.Save(path, project);
            _currentWorkspacePath = Path.GetFullPath(path);
            SetWorkspaceDirty(false);
            RuntimeLog.Info($"Workspace project saved. Path='{_currentWorkspacePath}'; Panes={project.Panes.Count}; Layout={project.LayoutMode}; ShowEndpointIds={project.ShowEndpointIds}");
            SetWorkspaceStatus($"Workspace saved: {_currentWorkspacePath}");
            return true;
        }
        catch (Exception ex)
        {
            RuntimeLog.Error($"Workspace project save failed. Path='{path}'", ex);
            MessageBox.Show(this, $"Unable to save Workspace project.\n\n{ex.Message}", "Save Workspace", MessageBoxButton.OK, MessageBoxImage.Error);
            SetWorkspaceStatus($"Save Workspace failed: {ex.Message}");
            return false;
        }
    }

    private WorkspaceProjectDocument CaptureWorkspaceProject()
    {
        RestoreMaximizedPaneIfNeeded();
        var bounds = WindowState == System.Windows.WindowState.Maximized ? RestoreBounds : new Rect(Left, Top, Width, Height);
        var project = new WorkspaceProjectDocument
        {
            ApplicationVersion = AppInfo.DisplayVersion,
            LayoutMode = _autoFitLayout ? WorkspaceLayoutMode.AutoFit : WorkspaceLayoutMode.FreeLayout,
            ShowEndpointIds = _showEndpointIds,
            Window = new WorkspaceWindowState
            {
                Width = bounds.Width,
                Height = bounds.Height,
                Left = bounds.Left,
                Top = bounds.Top,
                Maximized = WindowState == System.Windows.WindowState.Maximized
            }
        };

        foreach (var pane in GetAllPanes())
        {
            var position = GetPanePosition(pane);
            var width = double.IsNaN(pane.Width) ? pane.ActualWidth : pane.Width;
            var height = double.IsNaN(pane.Height) ? pane.ActualHeight : pane.Height;
            switch (pane)
            {
                case FilePane file:
                    project.Panes.Add(new WorkspacePaneState
                    {
                        Kind = PaneKind.File,
                        PaneId = file.Identity.PaneId,
                        DisplayIndex = file.Identity.DisplayIndex,
                        X = position.X,
                        Y = position.Y,
                        Width = width,
                        Height = height,
                        FilePath = file.CurrentPath
                    });
                    break;
                case BrowserTile browser:
                    project.Panes.Add(new WorkspacePaneState
                    {
                        Kind = PaneKind.Browser,
                        PaneId = browser.Identity.PaneId,
                        DisplayIndex = browser.Identity.DisplayIndex,
                        X = position.X,
                        Y = position.Y,
                        Width = width,
                        Height = height,
                        FilePath = null
                    });
                    break;
            }
        }

        project.Panes = project.Panes
            .OrderBy(pane => pane.Kind)
            .ThenBy(pane => pane.DisplayIndex)
            .ToList();
        return project;
    }

    private void ApplyWorkspaceProject(WorkspaceProjectDocument project, string sourcePath)
    {
        _suppressDirtyTracking = true;
        try
        {
            ClearWorkspacePanes();
            _workspaceInitialized = false;
            _showEndpointIds = project.ShowEndpointIds;
            _autoFitLayout = false; // prevent intermediate reflow while panes are reconstructed

            foreach (var saved in project.Panes.OrderBy(p => p.Kind).ThenBy(p => p.DisplayIndex))
            {
                var identity = new PaneIdentity(saved.PaneId == Guid.Empty ? Guid.NewGuid() : saved.PaneId, saved.Kind, saved.DisplayIndex);
                var position = new Point(Math.Max(0, saved.X), Math.Max(0, saved.Y));
                if (saved.Kind == PaneKind.File)
                {
                    var restoredPath = ResolveSavedFilePath(saved.FilePath, identity.Alias);
                    AddFilePane(restoredPath, position, Math.Max(380, saved.Width), Math.Max(340, saved.Height), identity);
                }
                else
                {
                    // Browser navigation/session state is intentionally not persisted. New Firefox windows start at Google.
                    AddBrowserPane(position, Math.Max(360, saved.Width), Math.Max(260, saved.Height), identity);
                }
            }

            _autoFitLayout = project.LayoutMode == WorkspaceLayoutMode.AutoFit;
            LayoutModeGlyph.Text = _autoFitLayout ? "▦" : "✥";
            LayoutModeButton.ToolTip = _autoFitLayout
                ? "Auto Fit layout is ON. Panes automatically fill the available Workspace. Click for Free Layout."
                : "Free Layout is ON. Panes keep saved/manual positions and sizes. Click for Auto Fit.";

            foreach (var browser in _browserPanes)
            {
                browser.SetEndpointIdVisibility(_showEndpointIds);
            }
            foreach (var file in _filePanes)
            {
                file.SetEndpointIdVisibility(_showEndpointIds);
            }
            ShowIdsButton.ToolTip = _showEndpointIds
                ? "Hide 64×64 routing endpoint badges (B1-B8 / F1-F4)"
                : "Show 64×64 routing endpoint badges (B1-B8 / F1-F4)";

            if (_autoFitLayout)
            {
                ApplyAutoFitLayout();
            }
            else
            {
                RecalculateCanvasExtentFromPanes();
            }

            RestoreWindowState(project.Window);
            _currentWorkspacePath = Path.GetFullPath(sourcePath);
            _workspaceInitialized = true;
            SetWorkspaceDirty(false);
            UpdatePaneCounts();
            RuntimeLog.Info($"Workspace project loaded. Path='{_currentWorkspacePath}'; Panes={project.Panes.Count}; Layout={project.LayoutMode}; ShowEndpointIds={project.ShowEndpointIds}");
            SetWorkspaceStatus($"Workspace loaded: {_currentWorkspacePath}");
        }
        finally
        {
            _suppressDirtyTracking = false;
        }
    }

    private static void ValidateWorkspaceProject(WorkspaceProjectDocument project)
    {
        var browserCount = project.Panes.Count(p => p.Kind == PaneKind.Browser);
        var fileCount = project.Panes.Count(p => p.Kind == PaneKind.File);
        if (browserCount > MaxBrowserPanes || fileCount > MaxFilePanes)
        {
            throw new InvalidDataException($"Workspace contains too many panes. Browser={browserCount}/{MaxBrowserPanes}, File={fileCount}/{MaxFilePanes}.");
        }

        var duplicateAlias = project.Panes
            .GroupBy(p => (p.Kind, p.DisplayIndex))
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicateAlias is not null)
        {
            throw new InvalidDataException($"Duplicate endpoint display index: {duplicateAlias.Key.Kind} {duplicateAlias.Key.DisplayIndex}.");
        }

        var duplicatePaneId = project.Panes.Where(p => p.PaneId != Guid.Empty).GroupBy(p => p.PaneId).FirstOrDefault(g => g.Count() > 1);
        if (duplicatePaneId is not null)
        {
            throw new InvalidDataException($"Duplicate PaneId: {duplicatePaneId.Key:D}.");
        }

        foreach (var pane in project.Panes)
        {
            var max = pane.Kind == PaneKind.Browser ? MaxBrowserPanes : MaxFilePanes;
            if (pane.DisplayIndex < 1 || pane.DisplayIndex > max)
            {
                throw new InvalidDataException($"Invalid {pane.Kind} display index {pane.DisplayIndex}. Allowed range is 1-{max}.");
            }
            if (!double.IsFinite(pane.X) || !double.IsFinite(pane.Y) || !double.IsFinite(pane.Width) || !double.IsFinite(pane.Height))
            {
                throw new InvalidDataException($"Pane {pane.Kind}{pane.DisplayIndex} contains non-finite geometry.");
            }
        }
    }

    private string ResolveSavedFilePath(string? savedPath, string alias)
    {
        if (!string.IsNullOrWhiteSpace(savedPath))
        {
            try
            {
                var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(savedPath));
                if (Directory.Exists(fullPath))
                {
                    return fullPath;
                }
                RuntimeLog.Warn($"[{alias}] Saved File path is unavailable. Path='{fullPath}'. Falling back to Desktop.");
            }
            catch (Exception ex)
            {
                RuntimeLog.Warn($"[{alias}] Saved File path is invalid. Path='{savedPath}'; Error={ex.Message}. Falling back to Desktop.");
            }
        }

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        return Directory.Exists(desktop) ? desktop : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private void RestoreWindowState(WorkspaceWindowState saved)
    {
        if (saved.Width >= MinWidth && saved.Height >= MinHeight && double.IsFinite(saved.Width) && double.IsFinite(saved.Height))
        {
            WindowState = System.Windows.WindowState.Normal;
            Width = saved.Width;
            Height = saved.Height;
            if (double.IsFinite(saved.Left) && double.IsFinite(saved.Top))
            {
                Left = saved.Left;
                Top = saved.Top;
            }
        }

        if (saved.Maximized)
        {
            WindowState = System.Windows.WindowState.Maximized;
        }
    }

    private void RecalculateCanvasExtentFromPanes()
    {
        var panes = GetAllPanes().ToList();
        if (panes.Count == 0)
        {
            WorkspaceCanvas.Width = Math.Max(900, WorkspaceScrollViewer.ViewportWidth);
            WorkspaceCanvas.Height = Math.Max(560, WorkspaceScrollViewer.ViewportHeight);
            return;
        }

        WorkspaceCanvas.Width = Math.Max(
            Math.Max(900, WorkspaceScrollViewer.ViewportWidth),
            panes.Max(p => GetPaneRect(p).Right + PaneGap));
        WorkspaceCanvas.Height = Math.Max(
            Math.Max(560, WorkspaceScrollViewer.ViewportHeight),
            panes.Max(p => GetPaneRect(p).Bottom + PaneGap));
    }

    private void ClearWorkspacePanes()
    {
        _maximizedPane = null;
        foreach (var tile in _browserPanes.ToArray())
        {
            tile.Shutdown();
            UnsubscribeBrowserPane(tile);
            WorkspaceCanvas.Children.Remove(tile);
        }
        foreach (var pane in _filePanes.ToArray())
        {
            UnsubscribeFilePane(pane);
            WorkspaceCanvas.Children.Remove(pane);
        }

        _browserPanes.Clear();
        _filePanes.Clear();
        _moveOrigins.Clear();
        WorkspaceCanvas.Children.Clear();
        WorkspaceCanvas.Width = 1200;
        WorkspaceCanvas.Height = 700;
        WorkspaceScrollViewer.ScrollToHorizontalOffset(0);
        WorkspaceScrollViewer.ScrollToVerticalOffset(0);
        UpdatePaneCounts();
    }

    private bool ConfirmReplaceWorkspace()
    {
        if (!_workspaceDirty)
        {
            return true;
        }

        var result = MessageBox.Show(
            this,
            "The current Workspace has unsaved layout/path changes. Save them first?",
            "AI Engineering Workspace",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        return result switch
        {
            MessageBoxResult.Yes => SaveCurrentWorkspace(),
            MessageBoxResult.No => true,
            _ => false
        };
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_closing)
        {
            return;
        }

        if (!ConfirmReplaceWorkspace())
        {
            e.Cancel = true;
            return;
        }

        ShutdownWorkspace();
    }

    private void MarkWorkspaceDirty(string reason)
    {
        if (_suppressDirtyTracking || !_workspaceInitialized)
        {
            return;
        }

        if (!_workspaceDirty)
        {
            _workspaceDirty = true;
            UpdateWindowTitle();
            RuntimeLog.Debug($"Workspace marked dirty. Reason='{reason}'");
        }
    }

    private void SetWorkspaceDirty(bool dirty)
    {
        _workspaceDirty = dirty;
        UpdateWindowTitle();
    }

    private void UpdateWindowTitle()
    {
        var projectName = string.IsNullOrWhiteSpace(_currentWorkspacePath)
            ? "Untitled Workspace"
            : Path.GetFileNameWithoutExtension(_currentWorkspacePath);
        var dirtyMarker = _workspaceDirty ? " *" : string.Empty;
        Title = $"AI Engineering Workspace — {AppInfo.DisplayVersion} — {projectName}{dirtyMarker}";
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
