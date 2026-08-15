using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using AIEngineeringWorkspace.FileManager;
using AIEngineeringWorkspace.Infrastructure;
using AIEngineeringWorkspace.Workspace;

namespace AIEngineeringWorkspace.Controls;

public partial class FilePane : UserControl
{
    private Point _dragStartPoint;
    private string _currentPath = string.Empty;

    public event Action<FilePane, string>? StatusChanged;
    public event Action<FilePane>? ClosePaneRequested;
    public event Action<FilePane>? MoveStarted;
    public event Action<FilePane, double, double>? MoveRequested;
    public event Action<FilePane>? MoveCompleted;
    public event Action<FilePane, double, double>? ResizeRequested;
    public event Action<FilePane>? ActivateRequested;

    internal PaneIdentity Identity { get; private set; } = PaneIdentity.Create(PaneKind.File, 1);
    public string PaneId => Identity.DisplayName;
    public string EndpointAlias => Identity.Alias;

    public FilePane()
    {
        InitializeComponent();
    }

    internal void Configure(PaneIdentity identity, string initialPath)
    {
        Identity = identity;
        PaneTitleTextBlock.Text = identity.DisplayName;
        EndpointBadgeTextBlock.Text = identity.Alias;
        EndpointBadgeBorder.ToolTip = $"Routing endpoint {identity.Alias}\nPaneId={identity.PaneId:D}";
        NavigateTo(initialPath);
        RuntimeLog.Info($"[{Identity.Alias}] File pane configured. PaneId={Identity.PaneId:D}; DisplayIndex={Identity.DisplayIndex}; InitialPath='{initialPath}'");
    }

    internal void SetEndpointIdVisibility(bool visible)
        => EndpointBadgeBorder.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    private void NavigateTo(string? requestedPath)
    {
        try
        {
            var path = Environment.ExpandEnvironmentVariables((requestedPath ?? string.Empty).Trim().Trim('"'));
            if (string.IsNullOrWhiteSpace(path))
            {
                path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }

            path = Path.GetFullPath(path);
            if (!Directory.Exists(path))
            {
                SetStatus($"Folder not found: {path}");
                return;
            }

            var entries = new List<FileEntry>();

            foreach (var directory in Directory.EnumerateDirectories(path))
            {
                try
                {
                    var info = new DirectoryInfo(directory);
                    entries.Add(new FileEntry
                    {
                        Name = info.Name,
                        FullPath = info.FullName,
                        IsDirectory = true,
                        Type = "Folder",
                        SizeText = string.Empty,
                        ModifiedText = info.LastWriteTime.ToString("yyyy/MM/dd HH:mm"),
                        Icon = ShellIconService.GetSmallIcon(info.FullName)
                    });
                }
                catch (Exception ex)
                {
                    RuntimeLog.Debug($"[{Identity.Alias}] Skipping directory '{directory}': {ex.Message}");
                }
            }

            foreach (var file in Directory.EnumerateFiles(path))
            {
                try
                {
                    var info = new FileInfo(file);
                    entries.Add(new FileEntry
                    {
                        Name = info.Name,
                        FullPath = info.FullName,
                        IsDirectory = false,
                        Type = string.IsNullOrWhiteSpace(info.Extension) ? "File" : info.Extension.TrimStart('.').ToUpperInvariant(),
                        SizeText = FormatSize(info.Length),
                        ModifiedText = info.LastWriteTime.ToString("yyyy/MM/dd HH:mm"),
                        Icon = ShellIconService.GetSmallIcon(info.FullName)
                    });
                }
                catch (Exception ex)
                {
                    RuntimeLog.Debug($"[{Identity.Alias}] Skipping file '{file}': {ex.Message}");
                }
            }

            _currentPath = path;
            PathTextBox.Text = path;
            FileListView.ItemsSource = entries
                .OrderByDescending(item => item.IsDirectory)
                .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            SetStatus($"{entries.Count(item => item.IsDirectory)} folder(s), {entries.Count(item => !item.IsDirectory)} file(s)");
            RuntimeLog.Info($"[{Identity.Alias}] Folder loaded. PaneId={Identity.PaneId:D}; Path='{path}'; Items={entries.Count}");
        }
        catch (UnauthorizedAccessException ex)
        {
            RuntimeLog.Warn($"[{Identity.Alias}] Access denied. Path='{requestedPath}'; Error={ex.Message}");
            SetStatus("Access denied.");
        }
        catch (Exception ex)
        {
            RuntimeLog.Error($"[{Identity.Alias}] Failed to load folder '{requestedPath}'.", ex);
            SetStatus($"Unable to load folder: {ex.Message}");
        }
    }

    private void GoButton_Click(object sender, RoutedEventArgs e) => NavigateTo(PathTextBox.Text);

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => NavigateTo(_currentPath);

    private void UpButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentPath))
        {
            return;
        }

        var parent = Directory.GetParent(_currentPath);
        if (parent is null)
        {
            SetStatus("Already at the drive root.");
            return;
        }

        NavigateTo(parent.FullName);
    }

    private void ClosePaneButton_Click(object sender, RoutedEventArgs e)
    {
        RuntimeLog.Info($"[{Identity.Alias}] File pane X clicked. PaneId={Identity.PaneId:D}; Path='{_currentPath}'");
        ClosePaneRequested?.Invoke(this);
    }

    private void PathTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            NavigateTo(PathTextBox.Text);
            e.Handled = true;
        }
    }

    private void FileListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FileListView.SelectedItem is not FileEntry entry)
        {
            return;
        }

        if (entry.IsDirectory)
        {
            NavigateTo(entry.FullPath);
            return;
        }

        SetStatus($"Selected file: {entry.Name}");
    }

    private void FileListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ActivateRequested?.Invoke(this);
        _dragStartPoint = e.GetPosition(FileListView);
    }

    private void FileListView_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || FileListView.SelectedItem is not FileEntry entry || entry.IsDirectory)
        {
            return;
        }

        var position = e.GetPosition(FileListView);
        if (Math.Abs(position.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        if (!File.Exists(entry.FullPath))
        {
            SetStatus("The selected file no longer exists.");
            return;
        }

        var data = new DataObject(DataFormats.FileDrop, new[] { entry.FullPath });
        RuntimeLog.Info($"[{Identity.Alias}] File drag started. PaneId={Identity.PaneId:D}; Path='{entry.FullPath}'");
        SetStatus($"Dragging: {entry.Name}");
        DragDrop.DoDragDrop(FileListView, data, DragDropEffects.Copy);
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

    private void SetStatus(string message)
    {
        PaneStatusTextBlock.Text = message;
        PaneStatusTextBlock.ToolTip = message;
        StatusChanged?.Invoke(this, message);
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        var kb = bytes / 1024d;
        if (kb < 1024)
        {
            return $"{kb:0.#} KB";
        }

        var mb = kb / 1024d;
        if (mb < 1024)
        {
            return $"{mb:0.#} MB";
        }

        return $"{mb / 1024d:0.##} GB";
    }
}
