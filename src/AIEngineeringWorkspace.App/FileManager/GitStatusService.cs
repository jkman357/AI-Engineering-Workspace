using System.Diagnostics;
using AIEngineeringWorkspace.Infrastructure;

namespace AIEngineeringWorkspace.FileManager;

internal static class GitStatusService
{
    private const int CommandTimeoutMs = 1800;

    internal static GitFolderSnapshot TryReadFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            return GitFolderSnapshot.NotRepository;
        }

        try
        {
            if (!HasGitMarkerInAncestry(folderPath))
            {
                return GitFolderSnapshot.NotRepository;
            }

            var repoRoot = RunGit(folderPath, "rev-parse --show-toplevel");
            if (!repoRoot.Success || string.IsNullOrWhiteSpace(repoRoot.Output))
            {
                return GitFolderSnapshot.NotRepository;
            }

            var root = Path.GetFullPath(repoRoot.Output.Trim());
            var statusResult = RunGit(root, "status --porcelain=v1 -z --untracked-files=all");
            var trackedResult = RunGit(root, "ls-files -z");
            if (!statusResult.Success || !trackedResult.Success)
            {
                return new GitFolderSnapshot(root, new Dictionary<string, GitPathState>(StringComparer.OrdinalIgnoreCase), false);
            }

            var changed = ParsePorcelain(root, statusResult.Output);
            var tracked = ParseNullSeparatedPaths(root, trackedResult.Output);
            var states = new Dictionary<string, GitPathState>(StringComparer.OrdinalIgnoreCase);

            foreach (var path in tracked)
            {
                MergeState(states, path, GitPathState.Clean);
            }

            foreach (var (path, state) in changed)
            {
                MergeState(states, path, state);
            }

            return new GitFolderSnapshot(root, states, true);
        }
        catch (Exception ex)
        {
            RuntimeLog.Warn($"Git status probe failed for '{folderPath}': {ex.Message}");
            return GitFolderSnapshot.NotRepository;
        }
    }

    internal static GitDecoration GetDecoration(GitFolderSnapshot snapshot, string fullPath, bool isDirectory)
    {
        if (!snapshot.IsRepository || string.IsNullOrWhiteSpace(snapshot.RepositoryRoot))
        {
            return GitDecoration.None;
        }

        var target = Normalize(fullPath);
        if (!isDirectory)
        {
            return snapshot.States.TryGetValue(target, out var exact)
                ? ToDecoration(exact)
                : GitDecoration.None;
        }

        var prefix = target.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var aggregate = GitPathState.None;
        foreach (var pair in snapshot.States)
        {
            if (pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                aggregate = HigherPriority(aggregate, pair.Value);
            }
        }

        return aggregate == GitPathState.None ? GitDecoration.None : ToDecoration(aggregate);
    }


    private static bool HasGitMarkerInAncestry(string folderPath)
    {
        try
        {
            var current = new DirectoryInfo(Path.GetFullPath(folderPath));
            while (current is not null)
            {
                var marker = Path.Combine(current.FullName, ".git");
                if (Directory.Exists(marker) || File.Exists(marker))
                {
                    return true;
                }

                current = current.Parent;
            }
        }
        catch
        {
        }

        return false;
    }

    private static Dictionary<string, GitPathState> ParsePorcelain(string root, string raw)
    {
        var result = new Dictionary<string, GitPathState>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(raw))
        {
            return result;
        }

        var records = raw.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < records.Length; i++)
        {
            var record = records[i];
            if (record.Length < 4)
            {
                continue;
            }

            var xy = record[..2];
            var pathPart = record[3..];
            if ((xy.Contains('R') || xy.Contains('C')) && i + 1 < records.Length)
            {
                pathPart = records[++i];
            }

            var state = StateFromCode(xy);
            var absolute = Normalize(Path.Combine(root, pathPart.Replace('/', Path.DirectorySeparatorChar)));
            MergeState(result, absolute, state);
        }

        return result;
    }

    private static HashSet<string> ParseNullSeparatedPaths(string root, string raw)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relative in raw.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            result.Add(Normalize(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar))));
        }

        return result;
    }

    private static GitPathState StateFromCode(string xy)
    {
        if (xy == "??")
        {
            return GitPathState.Added;
        }

        if (xy.Contains('D'))
        {
            return GitPathState.Deleted;
        }

        if (xy.Contains('A'))
        {
            return GitPathState.Added;
        }

        if (xy.Contains('M') || xy.Contains('R') || xy.Contains('C') || xy.Contains('U'))
        {
            return GitPathState.Modified;
        }

        return GitPathState.Modified;
    }

    private static void MergeState(Dictionary<string, GitPathState> states, string path, GitPathState incoming)
    {
        if (!states.TryGetValue(path, out var current))
        {
            states[path] = incoming;
            return;
        }

        states[path] = HigherPriority(current, incoming);
    }

    private static GitPathState HigherPriority(GitPathState a, GitPathState b)
        => Priority(b) > Priority(a) ? b : a;

    private static int Priority(GitPathState state)
        => state switch
        {
            GitPathState.Deleted => 4,
            GitPathState.Modified => 3,
            GitPathState.Added => 2,
            GitPathState.Clean => 1,
            _ => 0
        };

    private static GitDecoration ToDecoration(GitPathState state)
        => state switch
        {
            GitPathState.Clean => new GitDecoration("✓", "Git: tracked / clean"),
            GitPathState.Modified => new GitDecoration("!", "Git: modified"),
            GitPathState.Added => new GitDecoration("+", "Git: added / untracked"),
            GitPathState.Deleted => new GitDecoration("−", "Git: deleted"),
            _ => GitDecoration.None
        };

    private static string Normalize(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static GitCommandResult RunGit(string workingDirectory, string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git.exe",
                Arguments = $"-C \"{workingDirectory}\" {arguments}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        try
        {
            if (!process.Start())
            {
                return GitCommandResult.Failed;
            }
        }
        catch
        {
            return GitCommandResult.Failed;
        }

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(CommandTimeoutMs))
        {
            try { process.Kill(true); } catch { }
            return GitCommandResult.Failed;
        }

        Task.WaitAll(new Task[] { outputTask, errorTask }, 500);
        var output = outputTask.IsCompletedSuccessfully ? outputTask.Result : string.Empty;
        return process.ExitCode == 0 ? new GitCommandResult(true, output) : GitCommandResult.Failed;
    }

    private readonly record struct GitCommandResult(bool Success, string Output)
    {
        internal static GitCommandResult Failed => new(false, string.Empty);
    }
}

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
