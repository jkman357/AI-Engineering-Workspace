using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Interop;
using Microsoft.Win32;
using AIEngineeringWorkspace.Controls;
using AIEngineeringWorkspace.Browser;
using AIEngineeringWorkspace.Infrastructure;
using AIEngineeringWorkspace.Interop;
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
    private HwndSource? _mainHwndSource;
    private HwndSourceHook? _inputMessageHook;

    public MainWindow()
    {
        InitializeComponent();
        Title = $"AI Engineering Workspace — {AppInfo.DisplayVersion}";
        VersionTextBlock.Text = $"{AppInfo.DisplayVersion} — Zero-Mutation Firefox Baseline";
        SourceInitialized += (_, _) => InstallInputMessageDiagnostics();
        _healthTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _healthTimer.Tick += (_, _) => { foreach (var tile in _browserPanes.ToArray()) tile.CheckHealth(); };
        Loaded += (_, _) =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (!_workspaceInitialized) { CreateDefaultWorkspaceToFitViewport(); _workspaceInitialized = true; SetWorkspaceDirty(false); }
                RuntimeLog.Info($"MainWindow loaded. BrowserPaneCount={_browserPanes.Count}; FilePaneCount={_filePanes.Count}; DefaultBrowserUrl='{DefaultBrowserUrl}'; RuntimeLog='{RuntimeLog.CurrentPath}'");
                _healthTimer.Start();
                SetWorkspaceStatus("Ready. Auto Fit fills the Workspace; drag/resize switches to Free Layout. Browser □ maximizes a pane inside the Workspace.");
                UpdatePaneCounts();
            }, DispatcherPriority.Loaded);
        };
        WorkspaceScrollViewer.SizeChanged += (_, _) =>
        {
            if (_workspaceInitialized && _autoFitLayout && _maximizedPane is null)
            {
                Dispatcher.InvokeAsync(() =>
                {
                    ApplyAutoFitLayout();
                }, DispatcherPriority.Background);
            }
            SchedulePseudoDockGeometrySync("WorkspaceViewportSizeChanged");
        };
        WorkspaceScrollViewer.ScrollChanged += (_, _) => SchedulePseudoDockGeometrySync("WorkspaceScrollChanged");
        LocationChanged += (_, _) => SchedulePseudoDockGeometrySync("MainWindowLocationChanged");
        StateChanged += (_, _) =>
        {
            if (WindowState == System.Windows.WindowState.Minimized)
            {
                foreach (var browser in _browserPanes) browser.SetPseudoDockVisible(false);
                RuntimeLog.Info("MainWindow minimized; pseudo-docked Firefox top-level windows hidden.");
            }
            else
            {
                SchedulePseudoDockGeometrySync("MainWindowStateChanged");
            }
        };
        Activated += (_, _) => SchedulePseudoDockGeometrySync("MainWindowActivated");
        Closing += MainWindow_Closing;
    }

    private void InstallInputMessageDiagnostics()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        _mainHwndSource = HwndSource.FromHwnd(hwnd);
        if (_mainHwndSource is null)
        {
            RuntimeLog.Warn("Unable to install MainWindow input-language diagnostics because HwndSource is unavailable.");
            return;
        }

        _inputMessageHook = MainWindowInputMessageHook;
        _mainHwndSource.AddHook(_inputMessageHook);
        RuntimeLog.Info($"MainWindow input-language/IME message diagnostics installed. HWND=0x{hwnd.ToInt64():X}; WorkspaceThread={AIEngineeringWorkspace.Interop.NativeMethods.GetCurrentThreadId()}; WorkspaceHKL={InputLanguageDiagnostics.FormatHkl(AIEngineeringWorkspace.Interop.NativeMethods.GetKeyboardLayout(0))}");
    }

    private IntPtr MainWindowInputMessageHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        InputLanguageDiagnostics.LogWindowMessage("WPF.MainWindow", hwnd, msg, wParam, lParam);
        if ((uint)msg == NativeMethods.WM_INPUTLANGCHANGE)
        {
            FirefoxInputCoordinator.ObserveActiveInputLanguage(NativeMethods.GetCurrentThreadId(), "WPF.MainWindow.WM_INPUTLANGCHANGE.DiagnosticOnly");
        }
        return IntPtr.Zero;
    }

    private void RemoveInputMessageDiagnostics()
    {
        if (_mainHwndSource is not null && _inputMessageHook is not null)
        {
            _mainHwndSource.RemoveHook(_inputMessageHook);
        }

        _inputMessageHook = null;
        _mainHwndSource = null;
    }

    private void SchedulePseudoDockGeometrySync(string reason)
    {
        if (_closing || WindowState == System.Windows.WindowState.Minimized) return;
        Dispatcher.InvokeAsync(() =>
        {
            if (_closing || WindowState == System.Windows.WindowState.Minimized) return;
            foreach (var browser in _browserPanes.Where(x => x.HasDockedWindow))
            {
                browser.SetPseudoDockVisible(browser.IsVisible);
                if (browser.IsVisible) browser.FitBrowserToPane();
            }
            RuntimeLog.Debug($"Pseudo-dock geometry synchronization completed. Reason='{reason}'; BrowserCount={_browserPanes.Count(x => x.HasDockedWindow)}");
        }, DispatcherPriority.Render);
    }

    private void CreateDefaultWorkspaceToFitViewport()
    {
        AddFilePane(GetDefaultFilePath(1)); AddFilePane(GetDefaultFilePath(2));
        AddBrowserPane(); AddBrowserPane(); AddBrowserPane(); AddBrowserPane(); ApplyAutoFitLayout();
    }

    private static string GetDefaultFilePath(int slot)
    {
        var userProfile=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var preferred=slot switch{1=>Path.Combine(userProfile,"Downloads"),2=>Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),_=>userProfile};
        return Directory.Exists(preferred)?preferred:userProfile;
    }

    private int? AllocateDisplayIndex(PaneKind kind)
    {
        var max=kind==PaneKind.Browser?MaxBrowserPanes:MaxFilePanes;
        var used=kind==PaneKind.Browser?_browserPanes.Select(p=>p.Identity.DisplayIndex).ToHashSet():_filePanes.Select(p=>p.Identity.DisplayIndex).ToHashSet();
        for(var i=1;i<=max;i++)if(!used.Contains(i))return i;return null;
    }

    private BrowserTile? AddBrowserPane(Point? position=null,double width=620,double height=420,PaneIdentity? identityOverride=null)
    {
        var displayIndex=identityOverride?.DisplayIndex??AllocateDisplayIndex(PaneKind.Browser);if(displayIndex is null){SetWorkspaceStatus($"Browser pane limit reached ({MaxBrowserPanes}).");return null;}
        var identity=identityOverride??PaneIdentity.Create(PaneKind.Browser,displayIndex.Value);var tile=new BrowserTile();tile.Configure(identity,DefaultBrowserUrl);tile.SetEndpointIdVisibility(_showEndpointIds);
        tile.StatusChanged+=BrowserPane_StatusChanged;tile.ClosePaneRequested+=BrowserPane_ClosePaneRequested;tile.MoveStarted+=BrowserPane_MoveStarted;tile.MoveRequested+=BrowserPane_MoveRequested;tile.MoveCompleted+=BrowserPane_MoveCompleted;tile.ResizeRequested+=BrowserPane_ResizeRequested;tile.ActivateRequested+=BrowserPane_ActivateRequested;tile.MaximizeRequested+=BrowserPane_MaximizeRequested;tile.NativeBrowserActivated+=BrowserPane_NativeBrowserActivated;
        tile.Width=width;tile.Height=height;_browserPanes.Add(tile);WorkspaceCanvas.Children.Add(tile);SetPanePosition(tile,position??FindFreePosition(width,height));BringToFront(tile);EnsureCanvasBounds(tile);UpdatePaneCounts();if(_workspaceInitialized&&_autoFitLayout&&_maximizedPane is null)ApplyAutoFitLayout();return tile;
    }

    private FilePane? AddFilePane(string? initialPath=null,Point? position=null,double width=380,double height=340,PaneIdentity? identityOverride=null)
    {
        var displayIndex=identityOverride?.DisplayIndex??AllocateDisplayIndex(PaneKind.File);if(displayIndex is null){SetWorkspaceStatus($"File pane limit reached ({MaxFilePanes}).");return null;}
        var identity=identityOverride??PaneIdentity.Create(PaneKind.File,displayIndex.Value);var pane=new FilePane();pane.Configure(identity,initialPath??GetDefaultFilePath(displayIndex.Value));pane.SetEndpointIdVisibility(_showEndpointIds);
        pane.StatusChanged+=FilePane_StatusChanged;pane.ClosePaneRequested+=FilePane_ClosePaneRequested;pane.MoveStarted+=FilePane_MoveStarted;pane.MoveRequested+=FilePane_MoveRequested;pane.MoveCompleted+=FilePane_MoveCompleted;pane.ResizeRequested+=FilePane_ResizeRequested;pane.ActivateRequested+=FilePane_ActivateRequested;pane.PathChanged+=FilePane_PathChanged;
        pane.Width=width;pane.Height=height;_filePanes.Add(pane);WorkspaceCanvas.Children.Add(pane);SetPanePosition(pane,position??FindFreePosition(width,height));BringToFront(pane);EnsureCanvasBounds(pane);UpdatePaneCounts();if(_workspaceInitialized&&_autoFitLayout&&_maximizedPane is null)ApplyAutoFitLayout();return pane;
    }

    private void BrowserPane_ClosePaneRequested(BrowserTile tile)
    {
        if(_closing||!_browserPanes.Contains(tile))return;var alias=tile.Identity.Alias;if(ReferenceEquals(_maximizedPane,tile))ClearMaximizedPaneState(tile);tile.Shutdown();UnsubscribeBrowserPane(tile);WorkspaceCanvas.Children.Remove(tile);_browserPanes.Remove(tile);_moveOrigins.Remove(tile);UpdatePaneCounts();if(_autoFitLayout&&_maximizedPane is null)ApplyAutoFitLayout();MarkWorkspaceDirty($"Closed {alias}");SetWorkspaceStatus($"Closed {alias}. Its display index is now available for reuse.");
    }
    private void FilePane_ClosePaneRequested(FilePane pane)
    {
        if(_closing||!_filePanes.Contains(pane))return;var alias=pane.Identity.Alias;UnsubscribeFilePane(pane);WorkspaceCanvas.Children.Remove(pane);_filePanes.Remove(pane);_moveOrigins.Remove(pane);UpdatePaneCounts();if(_autoFitLayout&&_maximizedPane is null)ApplyAutoFitLayout();MarkWorkspaceDirty($"Closed {alias}");SetWorkspaceStatus($"Closed {alias}. Its display index is now available for reuse.");
    }
    private void UnsubscribeBrowserPane(BrowserTile tile){tile.StatusChanged-=BrowserPane_StatusChanged;tile.ClosePaneRequested-=BrowserPane_ClosePaneRequested;tile.MoveStarted-=BrowserPane_MoveStarted;tile.MoveRequested-=BrowserPane_MoveRequested;tile.MoveCompleted-=BrowserPane_MoveCompleted;tile.ResizeRequested-=BrowserPane_ResizeRequested;tile.ActivateRequested-=BrowserPane_ActivateRequested;tile.MaximizeRequested-=BrowserPane_MaximizeRequested;tile.NativeBrowserActivated-=BrowserPane_NativeBrowserActivated;}
    private void UnsubscribeFilePane(FilePane pane){pane.StatusChanged-=FilePane_StatusChanged;pane.ClosePaneRequested-=FilePane_ClosePaneRequested;pane.MoveStarted-=FilePane_MoveStarted;pane.MoveRequested-=FilePane_MoveRequested;pane.MoveCompleted-=FilePane_MoveCompleted;pane.ResizeRequested-=FilePane_ResizeRequested;pane.ActivateRequested-=FilePane_ActivateRequested;pane.PathChanged-=FilePane_PathChanged;}
    private void BrowserPane_StatusChanged(BrowserTile tile,string message)=>SetWorkspaceStatus($"{tile.Identity.Alias}: {message}");
    private void FilePane_StatusChanged(FilePane pane,string message)=>SetWorkspaceStatus($"{pane.Identity.Alias}: {message}");
    private void FilePane_PathChanged(FilePane pane,string path)=>MarkWorkspaceDirty($"{pane.Identity.Alias} path changed");
    private void BrowserPane_MoveStarted(BrowserTile tile)=>BeginPaneMove(tile); private void FilePane_MoveStarted(FilePane pane)=>BeginPaneMove(pane);
    private void BrowserPane_MoveRequested(BrowserTile tile,double dx,double dy)=>MovePane(tile,dx,dy); private void FilePane_MoveRequested(FilePane pane,double dx,double dy)=>MovePane(pane,dx,dy);
    private void BrowserPane_MoveCompleted(BrowserTile tile)=>CompletePaneMove(tile); private void FilePane_MoveCompleted(FilePane pane)=>CompletePaneMove(pane);
    private void BrowserPane_ResizeRequested(BrowserTile tile,PaneResizeDirection direction,double dx,double dy)=>ResizePane(tile,direction,dx,dy); private void FilePane_ResizeRequested(FilePane pane,PaneResizeDirection direction,double dx,double dy)=>ResizePane(pane,direction,dx,dy);
    private void BrowserPane_ActivateRequested(BrowserTile tile)
    {
        BringToFront(tile);
        if (tile.HasDockedWindow) tile.MarkBrowserActive($"{tile.Identity.Alias}.WpfChromeActivation");
    }
    private void BrowserPane_NativeBrowserActivated(BrowserTile tile)
    {
        if (!_browserPanes.Contains(tile)) return;
        BringToFront(tile);
        RuntimeLog.Debug($"[{tile.Identity.Alias}] Native Browser activation promoted pane z-order. BrowserHWND=0x{tile.BrowserHwnd.ToInt64():X}");
    }
    private void FilePane_ActivateRequested(FilePane pane)
    {
        FirefoxInputCoordinator.ClearActiveRoot($"{pane.Identity.Alias}.FilePaneActivation");
        BringToFront(pane);
    }
    private void BrowserPane_MaximizeRequested(BrowserTile tile)=>ToggleBrowserPaneMaximize(tile);

    private void BeginPaneMove(FrameworkElement pane){if(_autoFitLayout)SetLayoutMode(false,"Manual pane move");_moveOrigins[pane]=GetPanePosition(pane);BringToFront(pane);}
    private void MovePane(FrameworkElement pane,double dx,double dy){var p=GetPanePosition(pane);SetPanePosition(pane,new Point(Math.Max(0,p.X+dx),Math.Max(0,p.Y+dy)));EnsureCanvasBounds(pane);if(pane is BrowserTile browser)browser.FitBrowserToPane();}
    private void CompletePaneMove(FrameworkElement pane)
    {
        var alias=GetAlias(pane);var origin=_moveOrigins.TryGetValue(pane,out var saved)?saved:GetPanePosition(pane);_moveOrigins.Remove(pane);var rect=GetPaneRect(pane);
        var target=GetAllPanes().Where(x=>!ReferenceEquals(x,pane)).Select(x=>new{Pane=x,Area=IntersectionArea(rect,GetPaneRect(x))}).Where(x=>x.Area>0).OrderByDescending(x=>x.Area).FirstOrDefault();
        if(target is not null){var targetPosition=GetPanePosition(target.Pane);SetPanePosition(pane,targetPosition);SetPanePosition(target.Pane,origin);EnsureCanvasBounds(pane);EnsureCanvasBounds(target.Pane);SetWorkspaceStatus($"Swapped {alias} with {GetAlias(target.Pane)}. Pane identity did not change.");}else SetWorkspaceStatus($"Moved {alias} to {FormatPoint(GetPanePosition(pane))}.");
        MarkWorkspaceDirty($"Moved {alias}");
        SchedulePseudoDockGeometrySync($"{alias}.MoveCompleted");
    }

    private void ResizePane(FrameworkElement pane,PaneResizeDirection direction,double dx,double dy)
    {
        if(_autoFitLayout)SetLayoutMode(false,"Manual pane border resize");var width=double.IsNaN(pane.Width)?pane.ActualWidth:pane.Width;var height=double.IsNaN(pane.Height)?pane.ActualHeight:pane.Height;var p=GetPanePosition(pane);var left=p.X;var top=p.Y;
        var resizeLeft=direction is PaneResizeDirection.Left or PaneResizeDirection.TopLeft or PaneResizeDirection.BottomLeft;var resizeRight=direction is PaneResizeDirection.Right or PaneResizeDirection.TopRight or PaneResizeDirection.BottomRight;var resizeTop=direction is PaneResizeDirection.Top or PaneResizeDirection.TopLeft or PaneResizeDirection.TopRight;var resizeBottom=direction is PaneResizeDirection.Bottom or PaneResizeDirection.BottomLeft or PaneResizeDirection.BottomRight;
        if(resizeLeft){var d=dx;if(width-d<pane.MinWidth)d=width-pane.MinWidth;if(left+d<0)d=-left;left+=d;width-=d;}else if(resizeRight)width=Math.Max(pane.MinWidth,width+dx);
        if(resizeTop){var d=dy;if(height-d<pane.MinHeight)d=height-pane.MinHeight;if(top+d<0)d=-top;top+=d;height-=d;}else if(resizeBottom)height=Math.Max(pane.MinHeight,height+dy);
        pane.Width=width;pane.Height=height;SetPanePosition(pane,new Point(left,top));EnsureCanvasBounds(pane);if(pane is BrowserTile browser)browser.FitBrowserToPane();MarkWorkspaceDirty($"Resized {GetAlias(pane)}");
    }

    private void SetLayoutMode(bool autoFit,string reason)
    {
        if(_autoFitLayout==autoFit){if(autoFit)ApplyAutoFitLayout();return;}RestoreMaximizedPaneIfNeeded();_autoFitLayout=autoFit;LayoutModeGlyph.Text=autoFit?"▦":"✥";LayoutModeButton.ToolTip=autoFit?"Auto Fit layout is ON. Panes automatically fill the available Workspace. Click for Free Layout.":"Free Layout is ON. Panes keep manual positions/sizes. Click for Auto Fit.";if(autoFit)ApplyAutoFitLayout();MarkWorkspaceDirty($"Layout mode changed to {(autoFit?"Auto Fit":"Free Layout")}");UpdatePaneCounts();
    }

    private void ApplyAutoFitLayout()
    {
        if(!_autoFitLayout||_maximizedPane is not null)return;var panes=GetAllPanes().OrderBy(p=>p is FilePane?0:1).ThenBy(p=>p switch{FilePane f=>f.Identity.DisplayIndex,BrowserTile b=>b.Identity.DisplayIndex,_=>int.MaxValue}).ToList();
        var vw=WorkspaceScrollViewer.ViewportWidth>1?WorkspaceScrollViewer.ViewportWidth-2:Math.Max(1,WorkspaceScrollViewer.ActualWidth-2);var vh=WorkspaceScrollViewer.ViewportHeight>1?WorkspaceScrollViewer.ViewportHeight-2:Math.Max(1,WorkspaceScrollViewer.ActualHeight-2);
        var specs=panes.Select(p=>new AutoFitPaneSpec(Math.Max(1,p.MinWidth),Math.Max(1,p.MinHeight))).ToArray();var plan=AutoFitLayoutPlanner.Plan(vw,vh,specs,PaneGap,4);WorkspaceCanvas.Width=plan.CanvasWidth;WorkspaceCanvas.Height=plan.CanvasHeight;
        for(var i=0;i<panes.Count;i++){var pane=panes[i];var cell=plan.Cells[i];pane.Visibility=Visibility.Visible;pane.Width=cell.Width;pane.Height=cell.Height;SetPanePosition(pane,new Point(cell.X,cell.Y));if(pane is BrowserTile browser)browser.FitBrowserToPane();}
        WorkspaceScrollViewer.ScrollToHorizontalOffset(0);WorkspaceScrollViewer.ScrollToVerticalOffset(0);Dispatcher.InvokeAsync(()=>{foreach(var b in _browserPanes.Where(x=>x.Visibility==Visibility.Visible)){b.FitBrowserToPane();b.FinalizeBrowserRepaint();}},DispatcherPriority.Render);UpdatePaneCounts();
    }

    private void ToggleBrowserPaneMaximize(BrowserTile tile)
    {
        if(!_browserPanes.Contains(tile))return;
        if(ReferenceEquals(_maximizedPane,tile))
        {
            RestoreMaximizedPaneIfNeeded(true,$"{tile.Identity.Alias}.RestoreCompleted");
            return;
        }

        RestoreMaximizedPaneIfNeeded();
        _maximizedPane=tile;
        _maximizedPanePosition=GetPanePosition(tile);
        _maximizedPaneSize=new Size(double.IsNaN(tile.Width)?tile.ActualWidth:tile.Width,double.IsNaN(tile.Height)?tile.ActualHeight:tile.Height);
        foreach(var pane in GetAllPanes())pane.Visibility=ReferenceEquals(pane,tile)?Visibility.Visible:Visibility.Collapsed;
        var vw=Math.Max(900,WorkspaceScrollViewer.ViewportWidth>1?WorkspaceScrollViewer.ViewportWidth-4:WorkspaceScrollViewer.ActualWidth-8);
        var vh=Math.Max(560,WorkspaceScrollViewer.ViewportHeight>1?WorkspaceScrollViewer.ViewportHeight-4:WorkspaceScrollViewer.ActualHeight-8);
        WorkspaceCanvas.Width=vw;
        WorkspaceCanvas.Height=vh;
        SetPanePosition(tile,new Point(PaneGap,PaneGap));
        tile.Width=Math.Max(tile.MinWidth,vw-PaneGap*2);
        tile.Height=Math.Max(tile.MinHeight,vh-PaneGap*2);
        tile.SetMaximizedState(true);
        tile.FitBrowserToPane();
        BringToFront(tile);
        WorkspaceScrollViewer.ScrollToHorizontalOffset(0);
        WorkspaceScrollViewer.ScrollToVerticalOffset(0);
        ScheduleBrowserRepaintAfterLayout(tile,$"{tile.Identity.Alias}.MaximizeCompleted");
    }

    private void RestoreMaximizedPaneIfNeeded(bool restoreBrowserFocus=false,string? focusReason=null)
    {
        if(_maximizedPane is null)return;
        var pane=_maximizedPane;
        _maximizedPane=null;
        foreach(var item in GetAllPanes())item.Visibility=Visibility.Visible;
        if(pane is BrowserTile browser)browser.SetMaximizedState(false);
        if(_autoFitLayout)ApplyAutoFitLayout();
        else if(GetAllPanes().Contains(pane))
        {
            SetPanePosition(pane,_maximizedPanePosition);
            pane.Width=Math.Max(pane.MinWidth,_maximizedPaneSize.Width);
            pane.Height=Math.Max(pane.MinHeight,_maximizedPaneSize.Height);
            EnsureCanvasBounds(pane);
            if(pane is BrowserTile b)b.FitBrowserToPane();
        }

        if(restoreBrowserFocus && pane is BrowserTile restoredBrowser)
            ScheduleBrowserRepaintAfterLayout(restoredBrowser,focusReason??$"{restoredBrowser.Identity.Alias}.RestoreCompleted");
    }

    private void ScheduleBrowserRepaintAfterLayout(BrowserTile tile,string reason)
    {
        if(!_browserPanes.Contains(tile)||!tile.HasDockedWindow)return;
        RuntimeLog.Info($"[{tile.Identity.Alias}] Scheduling deferred Firefox repaint after Workspace layout transition without focus recovery. Reason='{reason}'");
        Dispatcher.InvokeAsync(() =>
        {
            if(!_browserPanes.Contains(tile)||tile.Visibility!=Visibility.Visible||!tile.HasDockedWindow)return;
            tile.FitBrowserToPane();
            tile.FinalizeBrowserRepaint();
            RuntimeLog.Info($"[{tile.Identity.Alias}] Deferred Firefox repaint completed; keyboard focus was not changed. Reason='{reason}'; BrowserHWND=0x{tile.BrowserHwnd.ToInt64():X}");
        },DispatcherPriority.ContextIdle);
    }

    private void ClearMaximizedPaneState(BrowserTile tile){if(!ReferenceEquals(_maximizedPane,tile))return;_maximizedPane=null;tile.SetMaximizedState(false);foreach(var p in GetAllPanes())p.Visibility=Visibility.Visible;}

    private Point FindFreePosition(double width,double height){const double step=36;var sw=Math.Max(WorkspaceCanvas.Width,1200);var sh=Math.Max(WorkspaceCanvas.Height,700);for(var y=PaneGap;y<=sh;y+=step)for(var x=PaneGap;x<=sw;x+=step){var candidate=new Rect(x,y,width,height);if(GetAllPanes().All(p=>!InflateRect(GetPaneRect(p),PaneGap).IntersectsWith(candidate)))return new Point(x,y);}WorkspaceCanvas.Height=sh+height+PaneGap*2;return new Point(PaneGap,sh+PaneGap);}
    private static Rect InflateRect(Rect source,double amount){source.Inflate(amount,amount);return source;}
    private IEnumerable<FrameworkElement> GetAllPanes()=>_filePanes.Cast<FrameworkElement>().Concat(_browserPanes);
    private static string GetAlias(FrameworkElement pane)=>pane switch{BrowserTile b=>b.Identity.Alias,FilePane f=>f.Identity.Alias,_=>"?"};
    private static Guid GetPaneId(FrameworkElement pane)=>pane switch{BrowserTile b=>b.Identity.PaneId,FilePane f=>f.Identity.PaneId,_=>Guid.Empty};
    private static Point GetPanePosition(FrameworkElement pane){var left=Canvas.GetLeft(pane);var top=Canvas.GetTop(pane);return new Point(double.IsNaN(left)?0:left,double.IsNaN(top)?0:top);}
    private static void SetPanePosition(FrameworkElement pane,Point position){Canvas.SetLeft(pane,position.X);Canvas.SetTop(pane,position.Y);}
    private static Rect GetPaneRect(FrameworkElement pane){var p=GetPanePosition(pane);var w=double.IsNaN(pane.Width)?pane.ActualWidth:pane.Width;var h=double.IsNaN(pane.Height)?pane.ActualHeight:pane.Height;return new Rect(p.X,p.Y,Math.Max(pane.MinWidth,w),Math.Max(pane.MinHeight,h));}
    private static double IntersectionArea(Rect a,Rect b){var i=Rect.Intersect(a,b);return i.IsEmpty?0:i.Width*i.Height;}
    private void BringToFront(FrameworkElement pane)=>Panel.SetZIndex(pane,++_zCounter);
    private void EnsureCanvasBounds(FrameworkElement pane){var r=GetPaneRect(pane);if(r.Right+PaneGap>WorkspaceCanvas.Width)WorkspaceCanvas.Width=r.Right+PaneGap;if(r.Bottom+PaneGap>WorkspaceCanvas.Height)WorkspaceCanvas.Height=r.Bottom+PaneGap;}
    private static string FormatPoint(Point point)=>$"({point.X:0},{point.Y:0})";

    private void AddFilePaneButton_Click(object sender,RoutedEventArgs e){RestoreMaximizedPaneIfNeeded();if(AddFilePane() is not null)MarkWorkspaceDirty("File pane added");}
    private void AddBrowserPaneButton_Click(object sender,RoutedEventArgs e)
    {
        RestoreMaximizedPaneIfNeeded();
        if(AddBrowserPane() is not null)
        {
            MarkWorkspaceDirty("Browser pane added");
        }
    }
    private void LayoutModeButton_Click(object sender,RoutedEventArgs e)
    {
        SetLayoutMode(!_autoFitLayout,"Toolbar toggle");
    }
    private void ShowIdsButton_Click(object sender,RoutedEventArgs e)
    {
        _showEndpointIds=!_showEndpointIds;
        foreach(var p in _browserPanes)p.SetEndpointIdVisibility(_showEndpointIds);
        foreach(var p in _filePanes)p.SetEndpointIdVisibility(_showEndpointIds);
        ShowIdsButton.ToolTip=_showEndpointIds?"Hide 64×64 routing endpoint badges (B1-B8 / F1-F4)":"Show 64×64 routing endpoint badges (B1-B8 / F1-F4)";
        MarkWorkspaceDirty("Endpoint ID display changed");
        SchedulePseudoDockGeometrySync(_showEndpointIds?"ShowIdsEnabled":"ShowIdsDisabled");
    }
    private void DetachAllButton_Click(object sender,RoutedEventArgs e){var count=_browserPanes.Count(x=>x.HasDockedWindow);foreach(var tile in _browserPanes.ToArray())if(tile.HasDockedWindow)tile.Detach();SetWorkspaceStatus(count==0?"Detach All: no Firefox windows were docked.":$"Detach All completed. Restored {count} Firefox window(s).");}
    private void UpdatePaneCounts(){var ba=string.Join(",",_browserPanes.OrderBy(p=>p.Identity.DisplayIndex).Select(p=>p.Identity.Alias));var fa=string.Join(",",_filePanes.OrderBy(p=>p.Identity.DisplayIndex).Select(p=>p.Identity.Alias));PaneCountTextBlock.Text=$"Standard user / API-free / {(_autoFitLayout?"Auto Fit":"Free Layout")} / {_browserPanes.Count} browser [{ba}] + {_filePanes.Count} file [{fa}]";}
    private void SetWorkspaceStatus(string message){WorkspaceStatusTextBlock.Text=message;WorkspaceStatusTextBlock.ToolTip=message;}

    private void NewWorkspaceButton_Click(object sender,RoutedEventArgs e){if(!ConfirmCreateNewWorkspace()){SetWorkspaceStatus("New Workspace canceled; the current Workspace was left unchanged.");return;}ResetWorkspaceToDefaults();}
    private void ResetWorkspaceToDefaults()
    {
        try{_suppressDirtyTracking=true;_newWorkspaceResetCount++;ClearWorkspacePanes();_zCounter=1;_currentWorkspacePath=null;_showEndpointIds=false;_autoFitLayout=true;_workspaceInitialized=false;CreateDefaultWorkspaceToFitViewport();_workspaceInitialized=true;LayoutModeGlyph.Text="▦";LayoutModeButton.ToolTip="Auto Fit layout is ON. Panes automatically fill the available Workspace. Click for Free Layout.";ShowIdsButton.ToolTip="Show 64×64 routing endpoint badges (B1-B8 / F1-F4)";SetWorkspaceDirty(false);var timestamp=DateTime.Now.ToString("HH:mm:ss");SetWorkspaceStatus($"New Workspace project created at {timestamp} (reset #{_newWorkspaceResetCount}). Browser panes launch Google; File paths use the standard defaults.");Dispatcher.InvokeAsync(ApplyAutoFitLayout,DispatcherPriority.Loaded);}finally{_suppressDirtyTracking=false;}
    }

    private void OpenWorkspaceButton_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFileDialog{Title="Open AI Engineering Workspace",Filter="AI Engineering Workspace (*.aew)|*.aew|JSON files (*.json)|*.json|All files (*.*)|*.*",DefaultExt=WorkspaceProjectService.DefaultExtension,CheckFileExists=true,Multiselect=false};if(dialog.ShowDialog(this)!=true||!ConfirmReplaceWorkspace())return;
        try{var project=WorkspaceProjectService.Load(dialog.FileName);ValidateWorkspaceProject(project);ApplyWorkspaceProject(project,dialog.FileName);}catch(Exception ex){RuntimeLog.Error($"Workspace project load failed. Path='{dialog.FileName}'",ex);MessageBox.Show(this,$"Unable to open Workspace project.\n\n{ex.Message}","Open Workspace",MessageBoxButton.OK,MessageBoxImage.Error);}
    }
    private void SaveWorkspaceButton_Click(object sender,RoutedEventArgs e)=>SaveCurrentWorkspace();private void SaveWorkspaceAsButton_Click(object sender,RoutedEventArgs e)=>SaveCurrentWorkspaceAs();
    private bool SaveCurrentWorkspace()=>string.IsNullOrWhiteSpace(_currentWorkspacePath)?SaveCurrentWorkspaceAs():SaveWorkspaceToPath(_currentWorkspacePath);
    private bool SaveCurrentWorkspaceAs(){var dialog=new SaveFileDialog{Title="Save AI Engineering Workspace",Filter="AI Engineering Workspace (*.aew)|*.aew|JSON files (*.json)|*.json",DefaultExt=WorkspaceProjectService.DefaultExtension,AddExtension=true,OverwritePrompt=true,FileName=string.IsNullOrWhiteSpace(_currentWorkspacePath)?"AI-Engineering-Workspace.aew":Path.GetFileName(_currentWorkspacePath)};return dialog.ShowDialog(this)==true&&SaveWorkspaceToPath(dialog.FileName);}
    private bool SaveWorkspaceToPath(string path){try{var project=CaptureWorkspaceProject();WorkspaceProjectService.Save(path,project);_currentWorkspacePath=Path.GetFullPath(path);SetWorkspaceDirty(false);SetWorkspaceStatus($"Workspace saved: {_currentWorkspacePath}");return true;}catch(Exception ex){MessageBox.Show(this,$"Unable to save Workspace project.\n\n{ex.Message}","Save Workspace",MessageBoxButton.OK,MessageBoxImage.Error);return false;}}

    private WorkspaceProjectDocument CaptureWorkspaceProject()
    {
        RestoreMaximizedPaneIfNeeded();var bounds=WindowState==System.Windows.WindowState.Maximized?RestoreBounds:new Rect(Left,Top,Width,Height);var project=new WorkspaceProjectDocument{ApplicationVersion=AppInfo.DisplayVersion,LayoutMode=_autoFitLayout?WorkspaceLayoutMode.AutoFit:WorkspaceLayoutMode.FreeLayout,ShowEndpointIds=_showEndpointIds,Window=new WorkspaceWindowState{Width=bounds.Width,Height=bounds.Height,Left=bounds.Left,Top=bounds.Top,Maximized=WindowState==System.Windows.WindowState.Maximized}};
        foreach(var pane in GetAllPanes()){var p=GetPanePosition(pane);var w=double.IsNaN(pane.Width)?pane.ActualWidth:pane.Width;var h=double.IsNaN(pane.Height)?pane.ActualHeight:pane.Height;if(pane is FilePane f)project.Panes.Add(new WorkspacePaneState{Kind=PaneKind.File,PaneId=f.Identity.PaneId,DisplayIndex=f.Identity.DisplayIndex,X=p.X,Y=p.Y,Width=w,Height=h,FilePath=f.CurrentPath});else if(pane is BrowserTile b)project.Panes.Add(new WorkspacePaneState{Kind=PaneKind.Browser,PaneId=b.Identity.PaneId,DisplayIndex=b.Identity.DisplayIndex,X=p.X,Y=p.Y,Width=w,Height=h});}
        project.Panes=project.Panes.OrderBy(p=>p.Kind).ThenBy(p=>p.DisplayIndex).ToList();return project;
    }

    private void ApplyWorkspaceProject(WorkspaceProjectDocument project,string sourcePath)
    {
        _suppressDirtyTracking=true;try{ClearWorkspacePanes();_workspaceInitialized=false;_showEndpointIds=project.ShowEndpointIds;_autoFitLayout=false;foreach(var saved in project.Panes.OrderBy(p=>p.Kind).ThenBy(p=>p.DisplayIndex)){var identity=new PaneIdentity(saved.PaneId==Guid.Empty?Guid.NewGuid():saved.PaneId,saved.Kind,saved.DisplayIndex);var pos=new Point(Math.Max(0,saved.X),Math.Max(0,saved.Y));if(saved.Kind==PaneKind.File)AddFilePane(ResolveSavedFilePath(saved.FilePath,identity.Alias),pos,Math.Max(380,saved.Width),Math.Max(340,saved.Height),identity);else AddBrowserPane(pos,Math.Max(360,saved.Width),Math.Max(260,saved.Height),identity);}_autoFitLayout=project.LayoutMode==WorkspaceLayoutMode.AutoFit;LayoutModeGlyph.Text=_autoFitLayout?"▦":"✥";foreach(var b in _browserPanes)b.SetEndpointIdVisibility(_showEndpointIds);foreach(var f in _filePanes)f.SetEndpointIdVisibility(_showEndpointIds);if(_autoFitLayout)ApplyAutoFitLayout();else RecalculateCanvasExtentFromPanes();RestoreWindowState(project.Window);_currentWorkspacePath=Path.GetFullPath(sourcePath);_workspaceInitialized=true;SetWorkspaceDirty(false);UpdatePaneCounts();SetWorkspaceStatus($"Workspace loaded: {_currentWorkspacePath}");}finally{_suppressDirtyTracking=false;}
    }

    private static void ValidateWorkspaceProject(WorkspaceProjectDocument project)
    {
        var bc=project.Panes.Count(p=>p.Kind==PaneKind.Browser);var fc=project.Panes.Count(p=>p.Kind==PaneKind.File);if(bc>MaxBrowserPanes||fc>MaxFilePanes)throw new InvalidDataException($"Workspace contains too many panes. Browser={bc}/{MaxBrowserPanes}, File={fc}/{MaxFilePanes}.");
        var duplicateAlias=project.Panes.GroupBy(p=>(p.Kind,p.DisplayIndex)).FirstOrDefault(g=>g.Count()>1);if(duplicateAlias is not null)throw new InvalidDataException($"Duplicate endpoint display index: {duplicateAlias.Key.Kind} {duplicateAlias.Key.DisplayIndex}.");
        var duplicateId=project.Panes.Where(p=>p.PaneId!=Guid.Empty).GroupBy(p=>p.PaneId).FirstOrDefault(g=>g.Count()>1);if(duplicateId is not null)throw new InvalidDataException($"Duplicate PaneId: {duplicateId.Key:D}.");
        foreach(var pane in project.Panes){var max=pane.Kind==PaneKind.Browser?MaxBrowserPanes:MaxFilePanes;if(pane.DisplayIndex<1||pane.DisplayIndex>max)throw new InvalidDataException($"Invalid {pane.Kind} display index {pane.DisplayIndex}.");if(!double.IsFinite(pane.X)||!double.IsFinite(pane.Y)||!double.IsFinite(pane.Width)||!double.IsFinite(pane.Height))throw new InvalidDataException($"Pane {pane.Kind}{pane.DisplayIndex} contains non-finite geometry.");}
    }

    private string ResolveSavedFilePath(string? savedPath,string alias){if(!string.IsNullOrWhiteSpace(savedPath)){try{var full=Path.GetFullPath(Environment.ExpandEnvironmentVariables(savedPath));if(Directory.Exists(full))return full;}catch{}}var desktop=Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);return Directory.Exists(desktop)?desktop:Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);}
    private void RestoreWindowState(WorkspaceWindowState saved){if(saved.Width>=MinWidth&&saved.Height>=MinHeight&&double.IsFinite(saved.Width)&&double.IsFinite(saved.Height)){WindowState=System.Windows.WindowState.Normal;Width=saved.Width;Height=saved.Height;if(double.IsFinite(saved.Left)&&double.IsFinite(saved.Top)){Left=saved.Left;Top=saved.Top;}}if(saved.Maximized)WindowState=System.Windows.WindowState.Maximized;}
    private void RecalculateCanvasExtentFromPanes(){var panes=GetAllPanes().ToList();if(panes.Count==0){WorkspaceCanvas.Width=Math.Max(900,WorkspaceScrollViewer.ViewportWidth);WorkspaceCanvas.Height=Math.Max(560,WorkspaceScrollViewer.ViewportHeight);return;}WorkspaceCanvas.Width=Math.Max(Math.Max(900,WorkspaceScrollViewer.ViewportWidth),panes.Max(p=>GetPaneRect(p).Right+PaneGap));WorkspaceCanvas.Height=Math.Max(Math.Max(560,WorkspaceScrollViewer.ViewportHeight),panes.Max(p=>GetPaneRect(p).Bottom+PaneGap));}
    private void ClearWorkspacePanes(){_maximizedPane=null;foreach(var tile in _browserPanes.ToArray()){tile.Shutdown();UnsubscribeBrowserPane(tile);WorkspaceCanvas.Children.Remove(tile);}foreach(var pane in _filePanes.ToArray()){UnsubscribeFilePane(pane);WorkspaceCanvas.Children.Remove(pane);}_browserPanes.Clear();_filePanes.Clear();_moveOrigins.Clear();WorkspaceCanvas.Children.Clear();WorkspaceCanvas.Width=1200;WorkspaceCanvas.Height=700;WorkspaceScrollViewer.ScrollToHorizontalOffset(0);WorkspaceScrollViewer.ScrollToVerticalOffset(0);UpdatePaneCounts();}

    private bool ConfirmCreateNewWorkspace()
    {
        if(_workspaceDirty){var dirty=MessageBox.Show(this,"The current Workspace has unsaved changes.\n\nSave changes before creating a new Workspace?","Create New Workspace",MessageBoxButton.YesNoCancel,MessageBoxImage.Question);return dirty switch{MessageBoxResult.Yes=>SaveCurrentWorkspace(),MessageBoxResult.No=>true,_=>false};}
        var result=MessageBox.Show(this,"Create a new Workspace project?\n\nThe current Workspace layout will be reset to the default configuration.","Create New Workspace",MessageBoxButton.YesNo,MessageBoxImage.Question);return result==MessageBoxResult.Yes;
    }
    private bool ConfirmReplaceWorkspace(){if(!_workspaceDirty)return true;var result=MessageBox.Show(this,"The current Workspace has unsaved layout/path changes. Save them first?","AI Engineering Workspace",MessageBoxButton.YesNoCancel,MessageBoxImage.Question);return result switch{MessageBoxResult.Yes=>SaveCurrentWorkspace(),MessageBoxResult.No=>true,_=>false};}
    private void MainWindow_Closing(object? sender,CancelEventArgs e){if(_closing)return;if(!ConfirmReplaceWorkspace()){e.Cancel=true;return;}ShutdownWorkspace();}
    private void MarkWorkspaceDirty(string reason){if(_suppressDirtyTracking||!_workspaceInitialized)return;if(!_workspaceDirty){_workspaceDirty=true;UpdateWindowTitle();RuntimeLog.Debug($"Workspace marked dirty. Reason='{reason}'");}}
    private void SetWorkspaceDirty(bool dirty){_workspaceDirty=dirty;UpdateWindowTitle();}
    private void UpdateWindowTitle(){var projectName=string.IsNullOrWhiteSpace(_currentWorkspacePath)?"Untitled Workspace":Path.GetFileNameWithoutExtension(_currentWorkspacePath);Title=$"AI Engineering Workspace — {AppInfo.DisplayVersion} — {projectName}{(_workspaceDirty?" *":"")}";}
    private void ShutdownWorkspace(){if(_closing)return;_closing=true;_healthTimer.Stop();RemoveInputMessageDiagnostics();foreach(var tile in _browserPanes.ToArray())tile.Shutdown();}
}
