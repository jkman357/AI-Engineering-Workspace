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

    private readonly List<FileEntry> _entries = new();
    private Point _dragStartPoint;
    private string _currentPath = string.Empty;
    private string _sortColumn = "Name";
    private bool _sortAscending = true;
    private CancellationTokenSource? _gitProbeCts;
    private int _navigationGeneration;

    public event Action<FilePane, string>? StatusChanged;
    public event Action<FilePane>? ClosePaneRequested;
    public event Action<FilePane>? MoveStarted;
    public event Action<FilePane, double, double>? MoveRequested;
    public event Action<FilePane>? MoveCompleted;
    public event Action<FilePane, PaneResizeDirection, double, double>? ResizeRequested;
    public event Action<FilePane>? ActivateRequested;

    internal PaneIdentity Identity { get; private set; } = PaneIdentity.Create(PaneKind.File, 1);
    public string PaneId => Identity.DisplayName;
    public string EndpointAlias => Identity.Alias;

    public FilePane()
    {
        InitializeComponent();
        Unloaded += (_, _) =>
        {
            _gitProbeCts?.Cancel();
            _gitProbeCts?.Dispose();
            _gitProbeCts = null;
        };
    }

    internal void Configure(PaneIdentity identity, string initialPath)
    {
        Identity = identity;
        PaneTitleTextBlock.Text = identity.DisplayName;
        EndpointBadgeTextBlock.Text = identity.Alias;
        EndpointBadgeBorder.ToolTip = $"Routing endpoint {identity.Alias}\nPaneId={identity.PaneId:D}";
        var badgeStyle = EndpointPalette.GetBadgeStyle(identity.Kind, identity.DisplayIndex);
        EndpointBadgeBorder.Background = badgeStyle.Background;
        EndpointBadgeBorder.BorderBrush = badgeStyle.Border;
        EndpointBadgeTextBlock.Foreground = badgeStyle.Foreground;
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

            _gitProbeCts?.Cancel();
            _gitProbeCts?.Dispose();
            _gitProbeCts = new CancellationTokenSource();
            var generation = ++_navigationGeneration;

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
                        SizeBytes = -1,
                        ModifiedText = info.LastWriteTime.ToString("yyyy/MM/dd HH:mm"),
                        ModifiedTime = info.LastWriteTime,
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
                        SizeBytes = info.Length,
                        ModifiedText = info.LastWriteTime.ToString("yyyy/MM/dd HH:mm"),
                        ModifiedTime = info.LastWriteTime,
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
            _entries.Clear();
            _entries.AddRange(entries);
            ApplySort(false);

            SetStatus($"{entries.Count(item => item.IsDirectory)} folder(s), {entries.Count(item => !item.IsDirectory)} file(s) | Git: scanning…");
            RuntimeLog.Info($"[{Identity.Alias}] Folder loaded without blocking Git probe. PaneId={Identity.PaneId:D}; Path='{path}'; Items={entries.Count}; Generation={generation}");
            _ = RefreshGitDecorationsAsync(path, generation, _gitProbeCts.Token);
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

    private async Task RefreshGitDecorationsAsync(string path, int generation, CancellationToken cancellationToken)
    {
        try
        {
            var targets = _entries.Select(item => new GitTarget(item.FullPath, item.IsDirectory)).ToArray();
            var result = await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var snapshot = GitStatusService.TryReadFolder(path, cancellationToken);
                var decorations = new Dictionary<string, GitDecoration>(StringComparer.OrdinalIgnoreCase);
                foreach (var target in targets)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    decorations[target.FullPath] = GitStatusService.GetDecoration(snapshot, target.FullPath, target.IsDirectory, cancellationToken);
                }
                return new GitDecorationRefresh(snapshot, decorations);
            }, cancellationToken);

            if (cancellationToken.IsCancellationRequested || generation != _navigationGeneration || !string.Equals(path, _currentPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await Dispatcher.InvokeAsync(() =>
            {
                if (generation != _navigationGeneration || !string.Equals(path, _currentPath, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                foreach (var entry in _entries)
                {
                    if (result.Decorations.TryGetValue(entry.FullPath, out var decoration))
                    {
                        entry.GitGlyph = decoration.Glyph;
                        entry.GitTooltip = decoration.Tooltip;
                    }
                }

                ApplySort(false);
                var gitSuffix = result.Snapshot.IsRepository ? $" | Git: {Path.GetFileName(result.Snapshot.RepositoryRoot)}" : string.Empty;
                SetStatus($"{_entries.Count(item => item.IsDirectory)} folder(s), {_entries.Count(item => !item.IsDirectory)} file(s){gitSuffix}");
                RuntimeLog.Info($"[{Identity.Alias}] Git decorations applied asynchronously. Path='{path}'; Generation={generation}; GitRepository={result.Snapshot.IsRepository}; GitRoot='{result.Snapshot.RepositoryRoot}'");
            });
        }
        catch (OperationCanceledException)
        {
            RuntimeLog.Debug($"[{Identity.Alias}] Git decoration scan canceled. Path='{path}'; Generation={generation}");
        }
        catch (Exception ex)
        {
            RuntimeLog.Warn($"[{Identity.Alias}] Background Git decoration failed for '{path}': {ex.Message}");
            if (generation == _navigationGeneration)
            {
                await Dispatcher.InvokeAsync(() => SetStatus($"{_entries.Count(item => item.IsDirectory)} folder(s), {_entries.Count(item => !item.IsDirectory)} file(s) | Git unavailable"));
            }
        }
    }

    private readonly record struct GitTarget(string FullPath, bool IsDirectory);
    private readonly record struct GitDecorationRefresh(GitFolderSnapshot Snapshot, IReadOnlyDictionary<string, GitDecoration> Decorations);

    private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not GridViewColumnHeader header || header.Tag is not string column)
        {
            return;
        }

        if (string.Equals(_sortColumn, column, StringComparison.OrdinalIgnoreCase))
        {
            _sortAscending = !_sortAscending;
        }
        else
        {
            _sortColumn = column;
            _sortAscending = true;
        }

        ApplySort(true);
    }

    private void ApplySort(bool updateStatus)
    {
        IOrderedEnumerable<FileEntry> ordered = _entries.OrderByDescending(item => item.IsDirectory);

        ordered = _sortColumn switch
        {
            "Type" => _sortAscending
                ? ordered.ThenBy(item => item.Type, StringComparer.CurrentCultureIgnoreCase).ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                : ordered.ThenByDescending(item => item.Type, StringComparer.CurrentCultureIgnoreCase).ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase),
            "Size" => _sortAscending
                ? ordered.ThenBy(item => item.SizeBytes).ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                : ordered.ThenByDescending(item => item.SizeBytes).ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase),
            "Modified" => _sortAscending
                ? ordered.ThenBy(item => item.ModifiedTime).ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                : ordered.ThenByDescending(item => item.ModifiedTime).ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => _sortAscending
                ? ordered.ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                : ordered.ThenByDescending(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
        };

        FileListView.ItemsSource = ordered.ToList();
        UpdateSortHeaders();

        if (updateStatus)
        {
            var direction = _sortAscending ? "ascending" : "descending";
            SetStatus($"Sorted by {_sortColumn} ({direction}).");
            RuntimeLog.Info($"[{Identity.Alias}] File list sorted. Column={_sortColumn}; Ascending={_sortAscending}; Path='{_currentPath}'");
        }
    }

    private void UpdateSortHeaders()
    {
        SetSortHeader(NameColumnHeader, "Name");
        SetSortHeader(TypeColumnHeader, "Type");
        SetSortHeader(SizeColumnHeader, "Size");
        SetSortHeader(ModifiedColumnHeader, "Modified");
    }

    private void SetSortHeader(GridViewColumnHeader header, string column)
    {
        header.Content = string.Equals(_sortColumn, column, StringComparison.OrdinalIgnoreCase)
            ? $"{column} {(_sortAscending ? "↑" : "↓")}"
            : column;
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

    private void FileListView_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var owner = Window.GetWindow(this);
        if (owner is null)
        {
            return;
        }

        var targetPath = FileListView.SelectedItem is FileEntry entry ? entry.FullPath : _currentPath;
        var screenPoint = FileListView.PointToScreen(e.GetPosition(FileListView));
        RuntimeLog.Info($"[{Identity.Alias}] Native Shell context menu requested. Path='{targetPath}'");

        if (!ShellContextMenuService.Show(owner, targetPath, screenPoint, out var error))
        {
            SetStatus($"Windows Shell menu unavailable: {error}");
        }
        else
        {
            SetStatus($"Windows Shell menu: {Path.GetFileName(targetPath.TrimEnd(Path.DirectorySeparatorChar))}");
            Dispatcher.BeginInvoke(() => NavigateTo(_currentPath), System.Windows.Threading.DispatcherPriority.Background);
        }

        e.Handled = true;
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

    private void PaneBorderResize_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not Thumb thumb ||
            thumb.Tag is not string directionText ||
            !Enum.TryParse<PaneResizeDirection>(directionText, out var direction))
        {
            return;
        }

        ActivateRequested?.Invoke(this);
        ResizeRequested?.Invoke(this, direction, e.HorizontalChange, e.VerticalChange);
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
