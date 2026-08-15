namespace AIEngineeringWorkspace.Workspace;

internal enum PaneKind
{
    Browser,
    File
}

internal sealed record PaneIdentity(Guid PaneId, PaneKind Kind, int DisplayIndex)
{
    public string Alias => Kind == PaneKind.Browser ? $"B{DisplayIndex}" : $"F{DisplayIndex}";
    public string DisplayName => Kind == PaneKind.Browser ? $"Browser {DisplayIndex}" : $"Files {DisplayIndex}";

    public static PaneIdentity Create(PaneKind kind, int displayIndex)
        => new(Guid.NewGuid(), kind, displayIndex);
}
