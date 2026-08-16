namespace AIEngineeringWorkspace.Workspace;

internal enum WorkspaceLayoutMode
{
    AutoFit,
    FreeLayout
}

internal sealed class WorkspaceProjectDocument
{
    public int FormatVersion { get; set; } = 1;
    public string ApplicationVersion { get; set; } = string.Empty;
    public DateTime SavedUtc { get; set; } = DateTime.UtcNow;
    public WorkspaceLayoutMode LayoutMode { get; set; } = WorkspaceLayoutMode.AutoFit;
    public bool ShowEndpointIds { get; set; }
    public WorkspaceWindowState Window { get; set; } = new();
    public List<WorkspacePaneState> Panes { get; set; } = new();
}

internal sealed class WorkspaceWindowState
{
    public double Width { get; set; }
    public double Height { get; set; }
    public double Left { get; set; }
    public double Top { get; set; }
    public bool Maximized { get; set; } = true;
}

internal sealed class WorkspacePaneState
{
    public PaneKind Kind { get; set; }
    public Guid PaneId { get; set; }
    public int DisplayIndex { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public string? FilePath { get; set; }
}
