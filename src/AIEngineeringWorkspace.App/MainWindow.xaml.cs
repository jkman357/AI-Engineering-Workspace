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
    private int _zCounter = 1;

    public MainWindow()
    {
        InitializeComponent();
        CreateDefaultWorkspace();

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
            RuntimeLog.Info($"MainWindow loaded. BrowserPaneCount={_browserPanes.Count}; FilePaneCount={_filePanes.Count}; DefaultBrowserUrl='{DefaultBrowserUrl}'; RuntimeLog='{RuntimeLog.CurrentPath}'");
            _healthTimer.Start();
            SetWorkspaceStatus("Ready. Drag ⋮⋮ to move panes; drag ◢ to resize; Show IDs exposes routing aliases.");
            UpdatePaneCounts();
        };

        Closing += (_, _) => ShutdownWorkspace();
    }

    private void CreateDefaultWorkspace()
    {
        AddFilePane(GetDefaultFilePath(1), new Point(8, 8), 360, 394);
        AddFilePane(GetDefaultFilePath(2), new Point(8, 410), 360, 394);

        AddBrowserPane(new Point(376, 8), 590, 394);
        AddBrowserPane(new Point(974, 8), 590, 394);
        AddBrowserPane(new Point(376, 410), 590, 394);
        AddBrowserPane(new Point(974, 410), 590, 394);
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
        tile.Width = width;
        tile.Height = height;

        _browserPanes.Add(tile);
        WorkspaceCanvas.Children.Add(tile);
        SetPanePosition(tile, position ?? FindFreePosition(width, height));
        BringToFront(tile);
        EnsureCanvasBounds(tile);
        UpdatePaneCounts();

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
        tile.Shutdown();
        UnsubscribeBrowserPane(tile);
        WorkspaceCanvas.Children.Remove(tile);
        _browserPanes.Remove(tile);
        _moveOrigins.Remove(tile);
        UpdatePaneCounts();
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

    private void BrowserPane_ResizeRequested(BrowserTile tile, double dw, double dh) => ResizePane(tile, dw, dh);
    private void FilePane_ResizeRequested(FilePane pane, double dw, double dh) => ResizePane(pane, dw, dh);

    private void BrowserPane_ActivateRequested(BrowserTile tile) => BringToFront(tile);
    private void FilePane_ActivateRequested(FilePane pane) => BringToFront(pane);

    private void BeginPaneMove(FrameworkElement pane)
    {
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

    private void ResizePane(FrameworkElement pane, double dw, double dh)
    {
        var currentWidth = double.IsNaN(pane.Width) ? pane.ActualWidth : pane.Width;
        var currentHeight = double.IsNaN(pane.Height) ? pane.ActualHeight : pane.Height;
        var desiredWidth = Math.Max(pane.MinWidth, currentWidth + dw);
        var desiredHeight = Math.Max(pane.MinHeight, currentHeight + dh);
        var position = GetPanePosition(pane);

        var maxWidth = double.PositiveInfinity;
        var maxHeight = double.PositiveInfinity;
        var currentVertical = new Rect(position.X, position.Y, currentWidth, currentHeight);

        foreach (var other in GetAllPanes().Where(other => !ReferenceEquals(other, pane)))
        {
            var otherRect = GetPaneRect(other);

            var verticalOverlap = Math.Min(currentVertical.Bottom, otherRect.Bottom) - Math.Max(currentVertical.Top, otherRect.Top);
            if (verticalOverlap > 0 && otherRect.Left >= position.X + pane.MinWidth)
            {
                maxWidth = Math.Min(maxWidth, otherRect.Left - position.X - PaneGap);
            }

            var horizontalOverlap = Math.Min(position.X + currentWidth, otherRect.Right) - Math.Max(position.X, otherRect.Left);
            if (horizontalOverlap > 0 && otherRect.Top >= position.Y + pane.MinHeight)
            {
                maxHeight = Math.Min(maxHeight, otherRect.Top - position.Y - PaneGap);
            }
        }

        if (!double.IsPositiveInfinity(maxWidth))
        {
            desiredWidth = Math.Min(desiredWidth, Math.Max(pane.MinWidth, maxWidth));
        }

        if (!double.IsPositiveInfinity(maxHeight))
        {
            desiredHeight = Math.Min(desiredHeight, Math.Max(pane.MinHeight, maxHeight));
        }

        pane.Width = desiredWidth;
        pane.Height = desiredHeight;
        EnsureCanvasBounds(pane);
        SetWorkspaceStatus($"Resized {GetAlias(pane)} to {desiredWidth:0}×{desiredHeight:0}.");
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

    private void AddBrowserPaneButton_Click(object sender, RoutedEventArgs e) => AddBrowserPane();

    private void AddFilePaneButton_Click(object sender, RoutedEventArgs e) => AddFilePane();

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

        ShowIdsButton.Content = _showEndpointIds ? "Hide IDs" : "Show IDs";
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
        PaneCountTextBlock.Text = $"Standard user / API-free / {_browserPanes.Count} browser [{browserAliases}] + {_filePanes.Count} file [{fileAliases}]";
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
