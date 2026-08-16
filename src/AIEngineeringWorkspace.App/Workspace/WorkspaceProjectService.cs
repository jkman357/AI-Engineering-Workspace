using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIEngineeringWorkspace.Workspace;

internal static class WorkspaceProjectService
{
    internal const string DefaultExtension = ".aew";
    internal const int CurrentFormatVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    internal static WorkspaceProjectDocument Load(string path)
    {
        var json = File.ReadAllText(path);
        var project = JsonSerializer.Deserialize<WorkspaceProjectDocument>(json, JsonOptions)
            ?? throw new InvalidDataException("Workspace project file is empty or invalid.");

        if (project.FormatVersion != CurrentFormatVersion)
        {
            throw new InvalidDataException($"Unsupported Workspace project format {project.FormatVersion}. Expected {CurrentFormatVersion}.");
        }

        project.Panes ??= new List<WorkspacePaneState>();
        return project;
    }

    internal static void Save(string path, WorkspaceProjectDocument project)
    {
        project.FormatVersion = CurrentFormatVersion;
        project.SavedUtc = DateTime.UtcNow;

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = fullPath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(project, JsonOptions));
        File.Move(tempPath, fullPath, true);
    }
}
