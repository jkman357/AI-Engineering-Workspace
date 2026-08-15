using System.Windows.Media;

namespace AIEngineeringWorkspace.FileManager;

internal sealed class FileEntry
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required bool IsDirectory { get; init; }
    public required string Type { get; init; }
    public required string SizeText { get; init; }
    public required string ModifiedText { get; init; }
    public ImageSource? Icon { get; init; }
}
