using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using AIEngineeringWorkspace.Browser;
using AIEngineeringWorkspace.Infrastructure;
using AIEngineeringWorkspace.Workspace;

namespace AIEngineeringWorkspace.Controls;

public partial class BrowserTile : UserControl
{
    private readonly FirefoxWindowService _firefox = new();
    private readonly Dictionary<IntPtr, BrowserWindowInfo> _workspaceLaunchedWindows = new();
    private CancellationTokenSource? _launchCts;
    private BrowserWindowInfo? _dockedWindow;

    public event Action<BrowserTile, string>? StatusChanged;
    public event Action<BrowserTile>? ClosePaneRequested;
    public event Action<BrowserTile>? MoveStarted;
    public event Action<BrowserTile, double, double>? MoveRequested;
    public event Action<BrowserTile>? MoveCompleted;
    public event Action<BrowserTile, double, double>? ResizeRequested;
    public event Action<BrowserTile>? ActivateRequested;

    internal PaneIdentity Identity { get; private set; } = PaneIdentity.Create(PaneKind.Browser, 1);
    public string TileId => Identity.DisplayName;
    public string EndpointAlias => Identity.Alias;
    public bool HasDockedWindow => BrowserHost.HasDockedWindow;
    public int WorkspaceLaunchedWindowCount => _workspaceLaunchedWindows.Count;

    public BrowserTile()
    {
        InitializeComponent();

        BrowserHost.DockedWindowLost += (_, _) =>
        {
            var lostHwnd = _dockedWindow?.Hwnd ?? IntPtr.Zero;
            if (lostHwnd != IntPtr.Zero)
            {
                _workspaceLaunchedWindows.Remove(lostHwnd);
            }

            _dockedWindow = null;
            UpdateBrowserControls();
            SetStatus("Docked Firefox was closed or became invalid. Pane recovered.");
        };
    }

    internal void Configure(PaneIdentity identity, string initialUrl)
    {
        Identity = identity;
        TileTitleTextBlock.Text = identity.DisplayName;
        EndpointBadgeTextBlock.Text = identity.Alias;
        EndpointBadgeBorder.ToolTip = $"Routing endpoint {identity.Alias}\nPaneId={identity.PaneId:D}";
        UrlTextBox.Text = initialUrl;
        UpdateBrowserControls();
        SetStatus("Ready.");
        RuntimeLog.Info($"[{Identity.Alias}] Browser pane configured. PaneId={Identity.PaneId:D}; DisplayIndex={Identity.DisplayIndex}; URL='{initialUrl}'");
    }

    internal void SetEndpointIdVisibility(bool visible)
        => EndpointBadgeBorder.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;


    internal void FitBrowserToPane()
        => BrowserHost.ResizeDockedWindow();

    public void CheckHealth()
    {
        if (!BrowserHost.HasDockedWindow)
        {
            return;
        }

        var trackedHwnd = BrowserHost.BrowserHwnd;
        if (!BrowserHost.CheckDockedWindowHealth())
        {
            if (_workspaceLaunchedWindows.Remove(trackedHwnd))
            {
                RuntimeLog.Info($"[{Identity.Alias}] Removed closed Workspace-launched Firefox from shutdown ownership. HWND=0x{trackedHwnd.ToInt64():X}");
            }

            _dockedWindow = null;
            UpdateBrowserControls();
        }
    }

    public void Shutdown()
    {
        RuntimeLog.Info($"[{Identity.Alias}] Shutdown requested. PaneId={Identity.PaneId:D}; WorkspaceLaunchedWindows={_workspaceLaunchedWindows.Count}; DockedHWND=0x{BrowserHost.BrowserHwnd.ToInt64():X}");

        _launchCts?.Cancel();
        _launchCts?.Dispose();
        _launchCts = null;

        BrowserHost.Detach();
        _dockedWindow = null;
        UpdateBrowserControls();

        foreach (var window in _workspaceLaunchedWindows.Values.ToArray())
        {
            _firefox.RequestCloseWindow(window, Identity.Alias);
        }

        _workspaceLaunchedWindows.Clear();
    }

    public void Detach()
    {
        if (!BrowserHost.HasDockedWindow)
        {
            SetStatus("Nothing is docked.");
            return;
        }

        var hwnd = BrowserHost.BrowserHwnd;
        var workspaceOwned = _workspaceLaunchedWindows.ContainsKey(hwnd);

        BrowserHost.Detach();
        _dockedWindow = null;
        UpdateBrowserControls();
        SetStatus(workspaceOwned
            ? "Firefox detached and restored. This Workspace-launched window will close when Workspace exits."
            : "Firefox detached and original window style/placement restored.");
    }

    private async void LaunchButton_Click(object sender, RoutedEventArgs e)
        => await LaunchAndDockAsync();

    private async Task LaunchAndDockAsync()
    {
        ActivateRequested?.Invoke(this);
        SetUiBusy(true);
        _launchCts?.Cancel();
        _launchCts?.Dispose();
        _launchCts = new CancellationTokenSource();

        try
        {
            var targetUrl = NormalizeUrl(UrlTextBox.Text);
            UrlTextBox.Text = targetUrl;
            SetStatus("Waiting for serialized Firefox launch / HWND discovery...");
            var window = await _firefox.LaunchAndFindNewWindowAsync(targetUrl, _launchCts.Token, Identity.Alias);
            if (window is null)
            {
                SetStatus("Firefox launched, but its new HWND could not be isolated safely. No existing window was guessed.");
                return;
            }

            _workspaceLaunchedWindows[window.Hwnd] = window;
            RuntimeLog.Info($"[{Identity.Alias}] Workspace launch ownership recorded. PaneId={Identity.PaneId:D}; PID={window.ProcessId}; HWND={window.HwndHex}; OwnedCount={_workspaceLaunchedWindows.Count}");

            DockWindow(window);
        }
        catch (OperationCanceledException)
        {
            RuntimeLog.Warn($"[{Identity.Alias}] Firefox launch/dock operation canceled.");
            SetStatus("Launch/dock canceled.");
        }
        catch (Exception ex)
        {
            RuntimeLog.Error($"[{Identity.Alias}] Launch + Dock failed.", ex);
            SetStatus($"Launch + Dock failed: {ex.Message}");
        }
        finally
        {
            SetUiBusy(false);
        }
    }

    private async void UrlTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        var targetUrl = NormalizeUrl(UrlTextBox.Text);
        UrlTextBox.Text = targetUrl;

        if (!BrowserHost.HasDockedWindow)
        {
            RuntimeLog.Info($"[{Identity.Alias}] URL Enter pressed without docked Firefox; launching target URL='{targetUrl}'.");
            await LaunchAndDockAsync();
            return;
        }

        try
        {
            ActivateRequested?.Invoke(this);
            SetStatus($"Navigating to {targetUrl}...");
            var sent = await BrowserHost.NavigateByKeyboardAsync(targetUrl);
            SetStatus(sent ? $"Navigation requested: {targetUrl}" : "Unable to navigate because the docked Firefox window is unavailable.");
        }
        catch (Exception ex)
        {
            RuntimeLog.Error($"[{Identity.Alias}] URL navigation failed. URL='{targetUrl}'", ex);
            SetStatus($"Navigation failed: {ex.Message}");
        }
    }

    private static string NormalizeUrl(string? input)
    {
        var value = (input ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return "https://www.google.com/";
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            return absolute.ToString();
        }

        var candidate = $"https://{value}";
        return Uri.TryCreate(candidate, UriKind.Absolute, out var normalized)
            ? normalized.ToString()
            : value;
    }

    private void DockExistingButton_Click(object sender, RoutedEventArgs e)
    {
        ActivateRequested?.Invoke(this);
        try
        {
            var windows = _firefox.FindFirefoxWindows();
            if (windows.Count == 0)
            {
                SetStatus("No visible top-level Firefox window found.");
                RuntimeLog.Warn($"[{Identity.Alias}] Dock Existing requested but no Firefox window was found.");
                return;
            }

            if (windows.Count > 1)
            {
                SetStatus($"{windows.Count} Firefox windows found. Refusing to guess which one belongs to this pane.");
                RuntimeLog.Warn($"[{Identity.Alias}] Dock Existing refused because multiple Firefox windows were found: {windows.Count}.");
                return;
            }

            var candidate = windows[0];
            RuntimeLog.Info($"[{Identity.Alias}] Dock Existing selected unique candidate. PID={candidate.ProcessId}; HWND={candidate.HwndHex}; Title='{candidate.Title}'");
            DockWindow(candidate);
        }
        catch (Exception ex)
        {
            RuntimeLog.Error($"[{Identity.Alias}] Dock Existing failed.", ex);
            SetStatus($"Dock Existing failed: {ex.Message}");
        }
    }

    private void FocusButton_Click(object sender, RoutedEventArgs e)
    {
        ActivateRequested?.Invoke(this);
        try
        {
            BrowserHost.FocusBrowser();
            SetStatus(BrowserHost.IsDocked ? "Focus requested for docked Firefox." : "Nothing is docked.");
        }
        catch (Exception ex)
        {
            RuntimeLog.Error($"[{Identity.Alias}] Focus request failed.", ex);
            SetStatus($"Focus failed: {ex.Message}");
        }
    }

    private void DetachButton_Click(object sender, RoutedEventArgs e)
    {
        ActivateRequested?.Invoke(this);
        Detach();
    }

    private void ClosePaneButton_Click(object sender, RoutedEventArgs e)
    {
        RuntimeLog.Info($"[{Identity.Alias}] Browser pane X clicked. PaneId={Identity.PaneId:D}; Docked={BrowserHost.HasDockedWindow}; WorkspaceOwnedWindows={_workspaceLaunchedWindows.Count}");
        ClosePaneRequested?.Invoke(this);
    }

    private void MoveThumb_DragStarted(object sender, DragStartedEventArgs e)
    {
        ActivateRequested?.Invoke(this);
        MoveStarted?.Invoke(this);
    }

    private void MoveThumb_DragDelta(object sender, DragDeltaEventArgs e)
        => MoveRequested?.Invoke(this, e.HorizontalChange, e.VerticalChange);

    private void MoveThumb_DragCompleted(object sender, DragCompletedEventArgs e)
        => MoveCompleted?.Invoke(this);

    private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        ActivateRequested?.Invoke(this);
        ResizeRequested?.Invoke(this, e.HorizontalChange, e.VerticalChange);
    }

    private void UserControl_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        => ActivateRequested?.Invoke(this);

    private void UserControl_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (BrowserHost.HasDockedWindow)
        {
            BrowserHost.ResizeDockedWindow();
        }
    }

    private void DockWindow(BrowserWindowInfo window)
    {
        BrowserHost.Dock(window.Hwnd);
        _dockedWindow = window;
        UpdateBrowserControls();
        SetStatus($"Docked PID={window.ProcessId}, HWND={window.HwndHex}, Title='{window.Title}'");
    }

    private void SetUiBusy(bool busy)
    {
        LaunchButton.IsEnabled = !busy;
        DockExistingButton.IsEnabled = !busy;
        ClosePaneButton.IsEnabled = !busy;
        MoveThumb.IsEnabled = !busy;
    }

    private void UpdateBrowserControls()
    {
        var hasBrowser = BrowserHost.HasDockedWindow;
        FocusButton.IsEnabled = hasBrowser;
        DetachButton.IsEnabled = hasBrowser;
    }

    private void SetStatus(string message)
    {
        TileStatusTextBlock.Text = message;
        TileStatusTextBlock.ToolTip = message;
        RuntimeLog.Info($"[{Identity.Alias}] UI status: {message}");
        StatusChanged?.Invoke(this, message);
    }
}
