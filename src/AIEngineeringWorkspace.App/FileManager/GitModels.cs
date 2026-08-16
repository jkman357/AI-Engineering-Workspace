namespace AIEngineeringWorkspace.FileManager;

internal enum GitPathState
{
    None,
    Clean,
    Added,
    Modified,
    Deleted
}

internal readonly record struct GitDecoration(string Glyph, string Tooltip)
{
    internal static GitDecoration None => new(string.Empty, string.Empty);
}

internal sealed record GitFolderSnapshot(string? RepositoryRoot, IReadOnlyDictionary<string, GitPathState> States, bool IsRepository)
{
    internal static GitFolderSnapshot NotRepository { get; } = new(null, new Dictionary<string, GitPathState>(), false);
}
