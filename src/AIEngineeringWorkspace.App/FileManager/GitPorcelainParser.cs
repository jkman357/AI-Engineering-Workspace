namespace AIEngineeringWorkspace.FileManager;

internal static class GitPorcelainParser
{
    internal static Dictionary<string, GitPathState> Parse(string root, string raw)
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
            var currentPath = record[3..];

            // porcelain v1 -z emits rename/copy as: "R  new-path\0old-path\0".
            // The first path is the destination/current path visible in the working tree.
            // The following source/old path must be consumed but must not replace it.
            if ((xy.Contains('R') || xy.Contains('C')) && i + 1 < records.Length)
            {
                i++;
            }

            var state = StateFromCode(xy);
            var absolute = Normalize(Path.Combine(root, currentPath.Replace('/', Path.DirectorySeparatorChar)));
            MergeState(result, absolute, state);
        }

        return result;
    }

    private static GitPathState StateFromCode(string xy)
    {
        if (xy == "??") return GitPathState.Added;
        if (xy.Contains('D')) return GitPathState.Deleted;
        if (xy.Contains('A')) return GitPathState.Added;
        if (xy.Contains('M') || xy.Contains('R') || xy.Contains('C') || xy.Contains('U')) return GitPathState.Modified;
        return GitPathState.Modified;
    }

    private static void MergeState(Dictionary<string, GitPathState> states, string path, GitPathState incoming)
    {
        if (!states.TryGetValue(path, out var current))
        {
            states[path] = incoming;
            return;
        }
        states[path] = Priority(incoming) > Priority(current) ? incoming : current;
    }

    private static int Priority(GitPathState state)
        => state switch
        {
            GitPathState.Deleted => 4,
            GitPathState.Modified => 3,
            GitPathState.Added => 2,
            GitPathState.Clean => 1,
            _ => 0
        };

    private static string Normalize(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
