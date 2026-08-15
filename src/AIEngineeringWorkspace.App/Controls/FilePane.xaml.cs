using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using AIEngineeringWorkspace.Dialogs;
using AIEngineeringWorkspace.FileManager;
using AIEngineeringWorkspace.Infrastructure;
using AIEngineeringWorkspace.Workspace;
using Microsoft.VisualBasic.FileIO;

namespace AIEngineeringWorkspace.Controls;

public partial class FilePane : UserControl
{
    private const string PreferredDropEffectFormat = "Preferred DropEffect";
    private const int DropEffectCopy = 1;
    private const int DropEffectMove = 2;

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
        if (FileListView.SelectedItem is FileEntry entry)
        {
            OpenEntry(entry);
        }
    }

    private void OpenEntry(FileEntry entry)
    {
        try
        {
            if (entry.IsDirectory)
            {
                NavigateTo(entry.FullPath);
                return;
            }

            if (!File.Exists(entry.FullPath))
            {
                SetStatus("The selected file no longer exists.");
                return;
            }

            Process.Start(new ProcessStartInfo(entry.FullPath) { UseShellExecute = true });
            RuntimeLog.Info($"[{Identity.Alias}] Shell open requested. Path='{entry.FullPath}'");
            SetStatus($"Opened: {entry.Name}");
        }
        catch (Exception ex)
        {
            RuntimeLog.Error($"[{Identity.Alias}] Shell open failed. Path='{entry.FullPath}'", ex);
            SetStatus($"Open failed: {ex.Message}");
        }
    }

    private void FileListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ActivateRequested?.Invoke(this);
        _dragStartPoint = e.GetPosition(FileListView);
    }

    private void FileListView_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        ActivateRequested?.Invoke(this);
        var item = ItemsControl.ContainerFromElement(FileListView, e.OriginalSource as DependencyObject) as ListViewItem;
        if (item is not null)
        {
            item.IsSelected = true;
            item.Focus();
        }
        else
        {
            FileListView.SelectedItem = null;
        }
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

    private void FileListView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C)
        {
            CopySelected(false);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.X)
        {
            CopySelected(true);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.V)
        {
            PasteClipboardItems();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            DeleteSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.F2)
        {
            RenameSelected();
            e.Handled = true;
        }
    }

    private void FileContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        var hasSelection = FileListView.SelectedItem is FileEntry;
        OpenMenuItem.IsEnabled = hasSelection;
        CopyMenuItem.IsEnabled = hasSelection;
        CutMenuItem.IsEnabled = hasSelection;
        RenameMenuItem.IsEnabled = hasSelection;
        DeleteMenuItem.IsEnabled = hasSelection;
        PasteMenuItem.IsEnabled = CanPasteFromClipboard();
    }

    private void OpenMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (FileListView.SelectedItem is FileEntry entry)
        {
            OpenEntry(entry);
        }
    }

    private void CopyMenuItem_Click(object sender, RoutedEventArgs e) => CopySelected(false);

    private void CutMenuItem_Click(object sender, RoutedEventArgs e) => CopySelected(true);

    private void PasteMenuItem_Click(object sender, RoutedEventArgs e) => PasteClipboardItems();

    private void RenameMenuItem_Click(object sender, RoutedEventArgs e) => RenameSelected();

    private void DeleteMenuItem_Click(object sender, RoutedEventArgs e) => DeleteSelected();

    private void NewFolderMenuItem_Click(object sender, RoutedEventArgs e) => CreateNewFolder();

    private void RefreshMenuItem_Click(object sender, RoutedEventArgs e) => NavigateTo(_currentPath);

    private void CopySelected(bool move)
    {
        if (FileListView.SelectedItem is not FileEntry entry)
        {
            return;
        }

        try
        {
            var data = new DataObject();
            data.SetData(DataFormats.FileDrop, new[] { entry.FullPath });
            data.SetData(PreferredDropEffectFormat, new MemoryStream(BitConverter.GetBytes(move ? DropEffectMove : DropEffectCopy)));
            Clipboard.SetDataObject(data, true);
            RuntimeLog.Info($"[{Identity.Alias}] Clipboard {(move ? "cut/move" : "copy")} set. Path='{entry.FullPath}'");
            SetStatus(move ? $"Cut / Move: {entry.Name}" : $"Copied: {entry.Name}");
        }
        catch (Exception ex)
        {
            RuntimeLog.Error($"[{Identity.Alias}] Clipboard operation failed. Path='{entry.FullPath}'", ex);
            SetStatus($"Clipboard failed: {ex.Message}");
        }
    }

    private void PasteClipboardItems()
    {
        if (string.IsNullOrWhiteSpace(_currentPath) || !Directory.Exists(_currentPath))
        {
            SetStatus("Current folder is unavailable.");
            return;
        }

        try
        {
            if (!CanPasteFromClipboard())
            {
                SetStatus("Clipboard does not contain files or folders.");
                return;
            }

            var sources = Clipboard.GetFileDropList().Cast<string>().Where(path => !string.IsNullOrWhiteSpace(path)).ToList();
            if (sources.Count == 0)
            {
                SetStatus("Clipboard does not contain files or folders.");
                return;
            }

            var move = ClipboardIndicatesMove();
            var completed = 0;
            var skipped = 0;

            foreach (var source in sources)
            {
                var trimmedSource = source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var name = Path.GetFileName(trimmedSource);
                if (string.IsNullOrWhiteSpace(name))
                {
                    skipped++;
                    continue;
                }

                var destination = Path.Combine(_currentPath, name);
                if (string.Equals(Path.GetFullPath(trimmedSource), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                    continue;
                }

                if (File.Exists(destination) || Directory.Exists(destination))
                {
                    RuntimeLog.Warn($"[{Identity.Alias}] Paste skipped because destination exists. Source='{source}'; Destination='{destination}'");
                    skipped++;
                    continue;
                }

                if (File.Exists(source))
                {
                    if (move)
                    {
                        FileSystem.MoveFile(source, destination);
                    }
                    else
                    {
                        FileSystem.CopyFile(source, destination, false);
                    }
                    completed++;
                }
                else if (Directory.Exists(source))
                {
                    if (move)
                    {
                        FileSystem.MoveDirectory(source, destination);
                    }
                    else
                    {
                        FileSystem.CopyDirectory(source, destination, false);
                    }
                    completed++;
                }
                else
                {
                    skipped++;
                }
            }

            RuntimeLog.Info($"[{Identity.Alias}] Paste completed. Mode={(move ? "Move" : "Copy")}; Destination='{_currentPath}'; Completed={completed}; Skipped={skipped}");
            NavigateTo(_currentPath);
            SetStatus($"{(move ? "Move" : "Copy")} completed: {completed}; skipped: {skipped}.");
        }
        catch (Exception ex)
        {
            RuntimeLog.Error($"[{Identity.Alias}] Paste failed. Destination='{_currentPath}'", ex);
            SetStatus($"Paste failed: {ex.Message}");
        }
    }

    private static bool CanPasteFromClipboard()
    {
        try
        {
            return Clipboard.ContainsFileDropList();
        }
        catch
        {
            return false;
        }
    }

    private static bool ClipboardIndicatesMove()
    {
        try
        {
            var raw = Clipboard.GetData(PreferredDropEffectFormat);
            byte[]? bytes = raw switch
            {
                MemoryStream stream => stream.ToArray(),
                byte[] array => array,
                _ => null
            };

            return bytes is { Length: >= 4 } && BitConverter.ToInt32(bytes, 0) == DropEffectMove;
        }
        catch
        {
            return false;
        }
    }

    private void RenameSelected()
    {
        if (FileListView.SelectedItem is not FileEntry entry)
        {
            return;
        }

        var dialog = new TextPromptDialog(Window.GetWindow(this), "Rename", "New name:", entry.Name);
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var newName = dialog.Value;
        if (string.IsNullOrWhiteSpace(newName) || newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            SetStatus("Invalid name.");
            return;
        }

        try
        {
            var parent = Path.GetDirectoryName(entry.FullPath) ?? _currentPath;
            var destination = Path.Combine(parent, newName);
            if (File.Exists(destination) || Directory.Exists(destination))
            {
                SetStatus("Rename target already exists.");
                return;
            }

            if (entry.IsDirectory)
            {
                Directory.Move(entry.FullPath, destination);
            }
            else
            {
                File.Move(entry.FullPath, destination);
            }

            RuntimeLog.Info($"[{Identity.Alias}] Renamed. Source='{entry.FullPath}'; Destination='{destination}'");
            NavigateTo(_currentPath);
            SetStatus($"Renamed to: {newName}");
        }
        catch (Exception ex)
        {
            RuntimeLog.Error($"[{Identity.Alias}] Rename failed. Path='{entry.FullPath}'", ex);
            SetStatus($"Rename failed: {ex.Message}");
        }
    }

    private void DeleteSelected()
    {
        if (FileListView.SelectedItem is not FileEntry entry)
        {
            return;
        }

        var answer = MessageBox.Show(
            Window.GetWindow(this),
            $"Move this {(entry.IsDirectory ? "folder" : "file")} to the Recycle Bin?\n\n{entry.Name}",
            "Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            if (entry.IsDirectory)
            {
                FileSystem.DeleteDirectory(entry.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            }
            else
            {
                FileSystem.DeleteFile(entry.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            }

            RuntimeLog.Info($"[{Identity.Alias}] Sent to Recycle Bin. Path='{entry.FullPath}'");
            NavigateTo(_currentPath);
            SetStatus($"Deleted to Recycle Bin: {entry.Name}");
        }
        catch (Exception ex)
        {
            RuntimeLog.Error($"[{Identity.Alias}] Delete failed. Path='{entry.FullPath}'", ex);
            SetStatus($"Delete failed: {ex.Message}");
        }
    }

    private void CreateNewFolder()
    {
        var dialog = new TextPromptDialog(Window.GetWindow(this), "New Folder", "Folder name:", "New folder");
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var name = dialog.Value;
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            SetStatus("Invalid folder name.");
            return;
        }

        try
        {
            var path = Path.Combine(_currentPath, name);
            if (Directory.Exists(path) || File.Exists(path))
            {
                SetStatus("An item with that name already exists.");
                return;
            }

            Directory.CreateDirectory(path);
            RuntimeLog.Info($"[{Identity.Alias}] Folder created. Path='{path}'");
            NavigateTo(_currentPath);
            SetStatus($"Created folder: {name}");
        }
        catch (Exception ex)
        {
            RuntimeLog.Error($"[{Identity.Alias}] New folder failed. BasePath='{_currentPath}'", ex);
            SetStatus($"New folder failed: {ex.Message}");
        }
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
