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
    public event Action<FilePane, string>? PathChanged;

    internal PaneIdentity Identity { get; private set; } = PaneIdentity.Create(PaneKind.File, 1);
    public string PaneId => Identity.DisplayName;
    public string EndpointAlias => Identity.Alias;
    internal string CurrentPath => _currentPath;

    public FilePane()
    {
        InitializeComponent();
        Unloaded += (_, _) => { _gitProbeCts?.Cancel(); _gitProbeCts?.Dispose(); _gitProbeCts = null; };
    }

    internal void Configure(PaneIdentity identity, string initialPath)
    {
        Identity = identity;
        PaneTitleTextBlock.Text = identity.DisplayName;
        EndpointBadgeTextBlock.Text = identity.Alias;
        EndpointLargeBadgeTextBlock.Text = identity.Alias;
        EndpointBadgeBorder.ToolTip = $"Routing endpoint {identity.Alias}\nPaneId={identity.PaneId:D}";
        var style = EndpointPalette.GetBadgeStyle(identity.Kind, identity.DisplayIndex);
        EndpointBadgeBorder.Background = style.Background; EndpointBadgeBorder.BorderBrush = style.Border; EndpointBadgeTextBlock.Foreground = style.Foreground;
        EndpointLargeBadgeBorder.Background = style.Background; EndpointLargeBadgeBorder.BorderBrush = style.Border; EndpointLargeBadgeTextBlock.Foreground = style.Foreground;
        NavigateTo(initialPath);
    }

    internal void SetEndpointIdVisibility(bool visible)
    {
        EndpointLargeBadgeBorder.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        PaneFrameBorder.BorderBrush = visible ? EndpointBadgeBorder.BorderBrush : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8A,0x8A,0x8A));
        PaneFrameBorder.BorderThickness = visible ? new Thickness(3) : new Thickness(1);
    }

    private void NavigateTo(string? requestedPath)
    {
        try
        {
            var path = Environment.ExpandEnvironmentVariables((requestedPath ?? string.Empty).Trim().Trim('"'));
            if (string.IsNullOrWhiteSpace(path)) path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            path = Path.GetFullPath(path);
            if (!Directory.Exists(path)) { SetStatus($"Folder not found: {path}"); return; }

            _gitProbeCts?.Cancel(); _gitProbeCts?.Dispose(); _gitProbeCts = new CancellationTokenSource();
            var generation = ++_navigationGeneration;
            var entries = new List<FileEntry>();
            foreach (var directory in Directory.EnumerateDirectories(path))
            {
                try
                {
                    var info = new DirectoryInfo(directory);
                    entries.Add(new FileEntry { Name=info.Name, FullPath=info.FullName, IsDirectory=true, Type="Folder", SizeText="", SizeBytes=-1, ModifiedText=info.LastWriteTime.ToString("yyyy/MM/dd HH:mm"), ModifiedTime=info.LastWriteTime, Icon=ShellIconService.GetSmallIcon(info.FullName) });
                }
                catch { }
            }
            foreach (var file in Directory.EnumerateFiles(path))
            {
                try
                {
                    var info = new FileInfo(file);
                    entries.Add(new FileEntry { Name=info.Name, FullPath=info.FullName, IsDirectory=false, Type=string.IsNullOrWhiteSpace(info.Extension)?"File":info.Extension.TrimStart('.').ToUpperInvariant(), SizeText=FormatSize(info.Length), SizeBytes=info.Length, ModifiedText=info.LastWriteTime.ToString("yyyy/MM/dd HH:mm"), ModifiedTime=info.LastWriteTime, Icon=ShellIconService.GetSmallIcon(info.FullName) });
                }
                catch { }
            }

            var changed = !string.Equals(_currentPath, path, StringComparison.OrdinalIgnoreCase);
            _currentPath = path; PathTextBox.Text = path; _entries.Clear(); _entries.AddRange(entries); ApplySort(false);
            SetStatus($"{entries.Count(x=>x.IsDirectory)} folder(s), {entries.Count(x=>!x.IsDirectory)} file(s) | Git: scanning…");
            _ = RefreshGitDecorationsAsync(path, generation, _gitProbeCts.Token);
            if (changed) PathChanged?.Invoke(this, path);
        }
        catch (UnauthorizedAccessException) { SetStatus("Access denied."); }
        catch (Exception ex) { RuntimeLog.Error($"[{Identity.Alias}] Failed to load folder '{requestedPath}'.", ex); SetStatus($"Unable to load folder: {ex.Message}"); }
    }

    private async Task RefreshGitDecorationsAsync(string path, int generation, CancellationToken token)
    {
        try
        {
            var targets = _entries.Select(x => (x.FullPath, x.IsDirectory)).ToArray();
            var result = await Task.Run(() =>
            {
                var snapshot = GitStatusService.TryReadFolder(path, token);
                var d = new Dictionary<string, GitDecoration>(StringComparer.OrdinalIgnoreCase);
                foreach (var t in targets) { token.ThrowIfCancellationRequested(); d[t.FullPath] = GitStatusService.GetDecoration(snapshot, t.FullPath, t.IsDirectory, token); }
                return (snapshot, d);
            }, token);
            if (token.IsCancellationRequested || generation != _navigationGeneration || !string.Equals(path,_currentPath,StringComparison.OrdinalIgnoreCase)) return;
            await Dispatcher.InvokeAsync(() =>
            {
                foreach (var entry in _entries) if (result.d.TryGetValue(entry.FullPath, out var deco)) { entry.GitGlyph=deco.Glyph; entry.GitTooltip=deco.Tooltip; }
                ApplySort(false);
                var suffix = result.snapshot.IsRepository ? $" | Git: {Path.GetFileName(result.snapshot.RepositoryRoot)}" : string.Empty;
                SetStatus($"{_entries.Count(x=>x.IsDirectory)} folder(s), {_entries.Count(x=>!x.IsDirectory)} file(s){suffix}");
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { RuntimeLog.Warn($"[{Identity.Alias}] Background Git decoration failed: {ex.Message}"); }
    }

    private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not GridViewColumnHeader h || h.Tag is not string c) return;
        if (string.Equals(_sortColumn,c,StringComparison.OrdinalIgnoreCase)) _sortAscending=!_sortAscending; else { _sortColumn=c; _sortAscending=true; }
        ApplySort(true);
    }

    private void ApplySort(bool updateStatus)
    {
        IOrderedEnumerable<FileEntry> ordered = _entries.OrderByDescending(x=>x.IsDirectory);
        ordered = _sortColumn switch
        {
            "Type" => _sortAscending ? ordered.ThenBy(x=>x.Type,StringComparer.CurrentCultureIgnoreCase).ThenBy(x=>x.Name,StringComparer.CurrentCultureIgnoreCase) : ordered.ThenByDescending(x=>x.Type,StringComparer.CurrentCultureIgnoreCase).ThenBy(x=>x.Name,StringComparer.CurrentCultureIgnoreCase),
            "Size" => _sortAscending ? ordered.ThenBy(x=>x.SizeBytes).ThenBy(x=>x.Name,StringComparer.CurrentCultureIgnoreCase) : ordered.ThenByDescending(x=>x.SizeBytes).ThenBy(x=>x.Name,StringComparer.CurrentCultureIgnoreCase),
            "Modified" => _sortAscending ? ordered.ThenBy(x=>x.ModifiedTime).ThenBy(x=>x.Name,StringComparer.CurrentCultureIgnoreCase) : ordered.ThenByDescending(x=>x.ModifiedTime).ThenBy(x=>x.Name,StringComparer.CurrentCultureIgnoreCase),
            _ => _sortAscending ? ordered.ThenBy(x=>x.Name,StringComparer.CurrentCultureIgnoreCase) : ordered.ThenByDescending(x=>x.Name,StringComparer.CurrentCultureIgnoreCase)
        };
        FileListView.ItemsSource = ordered.ToList();
        SetSortHeader(NameColumnHeader,"Name"); SetSortHeader(TypeColumnHeader,"Type"); SetSortHeader(SizeColumnHeader,"Size"); SetSortHeader(ModifiedColumnHeader,"Modified");
        if (updateStatus) SetStatus($"Sorted by {_sortColumn} ({(_sortAscending?"ascending":"descending")}).");
    }
    private void SetSortHeader(GridViewColumnHeader h,string c) => h.Content = string.Equals(_sortColumn,c,StringComparison.OrdinalIgnoreCase) ? $"{c} {(_sortAscending?"↑":"↓")}" : c;

    private void GoButton_Click(object s,RoutedEventArgs e)=>NavigateTo(PathTextBox.Text);
    private void RefreshButton_Click(object s,RoutedEventArgs e)=>NavigateTo(_currentPath);
    private void UpButton_Click(object s,RoutedEventArgs e) { var p=string.IsNullOrWhiteSpace(_currentPath)?null:Directory.GetParent(_currentPath); if(p is null) SetStatus("Already at the drive root."); else NavigateTo(p.FullName); }
    private void ClosePaneButton_Click(object s,RoutedEventArgs e)=>ClosePaneRequested?.Invoke(this);
    private void PathTextBox_KeyDown(object s,KeyEventArgs e){ if(e.Key==Key.Enter){NavigateTo(PathTextBox.Text);e.Handled=true;} }
    private void FileListView_MouseDoubleClick(object s,MouseButtonEventArgs e){ if(FileListView.SelectedItem is FileEntry x) OpenEntry(x); }

    private void OpenEntry(FileEntry entry)
    {
        try { if(entry.IsDirectory){NavigateTo(entry.FullPath);return;} Process.Start(new ProcessStartInfo(entry.FullPath){UseShellExecute=true}); SetStatus($"Opened: {entry.Name}"); }
        catch(Exception ex){SetStatus($"Open failed: {ex.Message}");}
    }

    private void FileListView_PreviewMouseLeftButtonDown(object s,MouseButtonEventArgs e){ActivateRequested?.Invoke(this);_dragStartPoint=e.GetPosition(FileListView);}
    private void FileListView_PreviewMouseRightButtonDown(object s,MouseButtonEventArgs e){ActivateRequested?.Invoke(this);var item=ItemsControl.ContainerFromElement(FileListView,e.OriginalSource as DependencyObject) as ListViewItem;if(item is not null){item.IsSelected=true;item.Focus();}else FileListView.SelectedItem=null;}
    private void FileListView_PreviewMouseRightButtonUp(object s,MouseButtonEventArgs e)
    {
        var owner=Window.GetWindow(this); if(owner is null)return;
        var target=FileListView.SelectedItem is FileEntry x?x.FullPath:_currentPath; var pt=FileListView.PointToScreen(e.GetPosition(FileListView));
        if(!ShellContextMenuService.Show(owner,target,pt,out var error))SetStatus($"Windows Shell menu unavailable: {error}"); else {SetStatus($"Windows Shell menu: {Path.GetFileName(target.TrimEnd(Path.DirectorySeparatorChar))}");Dispatcher.BeginInvoke(()=>NavigateTo(_currentPath),System.Windows.Threading.DispatcherPriority.Background);} e.Handled=true;
    }
    private void FileListView_PreviewMouseMove(object s,MouseEventArgs e)
    {
        if(e.LeftButton!=MouseButtonState.Pressed||FileListView.SelectedItem is not FileEntry x||x.IsDirectory)return;
        var p=e.GetPosition(FileListView);if(Math.Abs(p.X-_dragStartPoint.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(p.Y-_dragStartPoint.Y)<SystemParameters.MinimumVerticalDragDistance)return;
        if(!File.Exists(x.FullPath))return; var data=new DataObject(DataFormats.FileDrop,new[]{x.FullPath});DragDrop.DoDragDrop(FileListView,data,DragDropEffects.Copy);
    }

    private void FileListView_PreviewKeyDown(object s,KeyEventArgs e)
    {
        if(Keyboard.Modifiers==ModifierKeys.Control&&e.Key==Key.C){CopySelected(false);e.Handled=true;}
        else if(Keyboard.Modifiers==ModifierKeys.Control&&e.Key==Key.X){CopySelected(true);e.Handled=true;}
        else if(Keyboard.Modifiers==ModifierKeys.Control&&e.Key==Key.V){PasteClipboardItems();e.Handled=true;}
        else if(e.Key==Key.Delete){DeleteSelected();e.Handled=true;}
        else if(e.Key==Key.F2){RenameSelected();e.Handled=true;}
    }

    private void CopySelected(bool move)
    {
        if(FileListView.SelectedItem is not FileEntry x)return;
        var data=new DataObject();data.SetData(DataFormats.FileDrop,new[]{x.FullPath});data.SetData(PreferredDropEffectFormat,new MemoryStream(BitConverter.GetBytes(move?DropEffectMove:DropEffectCopy)));Clipboard.SetDataObject(data,true);SetStatus(move?$"Cut / Move: {x.Name}":$"Copied: {x.Name}");
    }

    private void PasteClipboardItems()
    {
        try
        {
            if(!Clipboard.ContainsFileDropList()){SetStatus("Clipboard does not contain files or folders.");return;}
            var move=ClipboardIndicatesMove();int done=0,skip=0;
            foreach(var source in Clipboard.GetFileDropList().Cast<string>())
            {
                var name=Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar));if(string.IsNullOrWhiteSpace(name)){skip++;continue;}
                var dest=Path.Combine(_currentPath,name);if(File.Exists(dest)||Directory.Exists(dest)){skip++;continue;}
                if(File.Exists(source)){if(move)FileSystem.MoveFile(source,dest);else FileSystem.CopyFile(source,dest,false);done++;}
                else if(Directory.Exists(source)){if(move)FileSystem.MoveDirectory(source,dest);else FileSystem.CopyDirectory(source,dest,false);done++;}else skip++;
            }
            NavigateTo(_currentPath);SetStatus($"{(move?"Move":"Copy")} completed: {done}; skipped: {skip}.");
        }
        catch(Exception ex){SetStatus($"Paste failed: {ex.Message}");}
    }
    private static bool ClipboardIndicatesMove(){try{var raw=Clipboard.GetData(PreferredDropEffectFormat);var b=raw switch{MemoryStream m=>m.ToArray(),byte[] a=>a,_=>null};return b is {Length:>=4}&&BitConverter.ToInt32(b,0)==DropEffectMove;}catch{return false;}}

    private void RenameSelected()
    {
        if(FileListView.SelectedItem is not FileEntry x)return;var dialog=new TextPromptDialog(Window.GetWindow(this),"Rename","New name:",x.Name);if(dialog.ShowDialog()!=true)return;
        var n=dialog.Value;if(string.IsNullOrWhiteSpace(n)||n.IndexOfAny(Path.GetInvalidFileNameChars())>=0){SetStatus("Invalid name.");return;}
        try{var dest=Path.Combine(Path.GetDirectoryName(x.FullPath)??_currentPath,n);if(File.Exists(dest)||Directory.Exists(dest)){SetStatus("Rename target already exists.");return;}if(x.IsDirectory)Directory.Move(x.FullPath,dest);else File.Move(x.FullPath,dest);NavigateTo(_currentPath);SetStatus($"Renamed to: {n}");}catch(Exception ex){SetStatus($"Rename failed: {ex.Message}");}
    }

    private void DeleteSelected()
    {
        if(FileListView.SelectedItem is not FileEntry x)return;var answer=MessageBox.Show(Window.GetWindow(this),$"Move this {(x.IsDirectory?"folder":"file")} to the Recycle Bin?\n\n{x.Name}","Delete",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No);if(answer!=MessageBoxResult.Yes)return;
        try{if(x.IsDirectory)FileSystem.DeleteDirectory(x.FullPath,UIOption.OnlyErrorDialogs,RecycleOption.SendToRecycleBin);else FileSystem.DeleteFile(x.FullPath,UIOption.OnlyErrorDialogs,RecycleOption.SendToRecycleBin);NavigateTo(_currentPath);SetStatus($"Deleted to Recycle Bin: {x.Name}");}catch(Exception ex){SetStatus($"Delete failed: {ex.Message}");}
    }

    private void MoveThumb_DragStarted(object s,DragStartedEventArgs e){ActivateRequested?.Invoke(this);MoveStarted?.Invoke(this);}
    private void MoveThumb_DragDelta(object s,DragDeltaEventArgs e)=>MoveRequested?.Invoke(this,e.HorizontalChange,e.VerticalChange);
    private void MoveThumb_DragCompleted(object s,DragCompletedEventArgs e)=>MoveCompleted?.Invoke(this);
    private void PaneBorderResize_DragDelta(object s,DragDeltaEventArgs e){if(s is Thumb t&&t.Tag is string d&&Enum.TryParse<PaneResizeDirection>(d,out var dir)){ActivateRequested?.Invoke(this);ResizeRequested?.Invoke(this,dir,e.HorizontalChange,e.VerticalChange);}}
    private void UserControl_PreviewMouseDown(object s,MouseButtonEventArgs e)=>ActivateRequested?.Invoke(this);
    private void SetStatus(string message){PaneStatusTextBlock.Text=message;PaneStatusTextBlock.ToolTip=message;StatusChanged?.Invoke(this,message);}
    private static string FormatSize(long bytes){if(bytes<1024)return $"{bytes} B";var kb=bytes/1024d;if(kb<1024)return $"{kb:0.#} KB";var mb=kb/1024d;if(mb<1024)return $"{mb:0.#} MB";return $"{mb/1024d:0.##} GB";}
}
