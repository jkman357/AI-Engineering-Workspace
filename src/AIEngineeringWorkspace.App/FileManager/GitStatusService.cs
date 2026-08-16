using System.Collections.Concurrent;
using System.Diagnostics;
using AIEngineeringWorkspace.Infrastructure;

namespace AIEngineeringWorkspace.FileManager;

internal static class GitStatusService
{
    private const int CommandTimeoutMs = 1800;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(5);
    private static readonly ConcurrentDictionary<string, CacheEntry> SnapshotCache = new(StringComparer.OrdinalIgnoreCase);

    internal static GitFolderSnapshot TryReadFolder(string folderPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath)) return GitFolderSnapshot.NotRepository;
        var cacheKey = Normalize(folderPath);
        if (SnapshotCache.TryGetValue(cacheKey, out var cached) && cached.ExpiresUtc > DateTime.UtcNow) return cached.Snapshot;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!HasGitMarkerInAncestry(folderPath))
            {
                var none = GitFolderSnapshot.NotRepository; SnapshotCache[cacheKey] = new CacheEntry(DateTime.UtcNow + CacheLifetime, none); return none;
            }
            var repoRoot = RunGit(folderPath, "rev-parse --show-toplevel", cancellationToken);
            if (!repoRoot.Success || string.IsNullOrWhiteSpace(repoRoot.Output)) return GitFolderSnapshot.NotRepository;
            var root = Path.GetFullPath(repoRoot.Output.Trim());
            var status = RunGit(root, "status --porcelain=v1 -z --untracked-files=all", cancellationToken);
            var tracked = RunGit(root, "ls-files -z", cancellationToken);
            if (!status.Success || !tracked.Success) return new GitFolderSnapshot(root, new Dictionary<string, GitPathState>(StringComparer.OrdinalIgnoreCase), false);
            var changed = GitPorcelainParser.Parse(root, status.Output);
            var states = new Dictionary<string, GitPathState>(StringComparer.OrdinalIgnoreCase);
            foreach (var relative in tracked.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries)) MergeState(states, Normalize(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar))), GitPathState.Clean);
            foreach (var pair in changed) MergeState(states, pair.Key, pair.Value);
            var snapshot = new GitFolderSnapshot(root, states, true);
            SnapshotCache[cacheKey] = new CacheEntry(DateTime.UtcNow + CacheLifetime, snapshot);
            SnapshotCache[Normalize(root)] = new CacheEntry(DateTime.UtcNow + CacheLifetime, snapshot);
            return snapshot;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { RuntimeLog.Warn($"Git status probe failed for '{folderPath}': {ex.Message}"); return GitFolderSnapshot.NotRepository; }
    }

    internal static GitDecoration GetDecoration(GitFolderSnapshot snapshot, string fullPath, bool isDirectory, CancellationToken cancellationToken = default)
    {
        var target = Normalize(fullPath);
        if (!snapshot.IsRepository || string.IsNullOrWhiteSpace(snapshot.RepositoryRoot)) return isDirectory ? TryGetRepositoryRootDecoration(target, cancellationToken) : GitDecoration.None;
        if (!isDirectory) return snapshot.States.TryGetValue(target, out var exact) ? ToDecoration(exact) : GitDecoration.None;
        if (string.Equals(target, Normalize(snapshot.RepositoryRoot), StringComparison.OrdinalIgnoreCase)) return SummarizeRepository(snapshot);
        var prefix = target.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var aggregate = GitPathState.None;
        foreach (var pair in snapshot.States) if (pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) aggregate = HigherPriority(aggregate, pair.Value);
        return aggregate == GitPathState.None ? GitDecoration.None : ToDecoration(aggregate);
    }

    private static GitDecoration TryGetRepositoryRootDecoration(string directoryPath, CancellationToken cancellationToken)
    {
        if (!HasDirectGitMarker(directoryPath)) return GitDecoration.None;
        cancellationToken.ThrowIfCancellationRequested();
        var repo = TryReadFolder(directoryPath, cancellationToken);
        return repo.IsRepository ? SummarizeRepository(repo) : new GitDecoration("G", "Git repository detected; working-tree status unavailable");
    }

    private static GitDecoration SummarizeRepository(GitFolderSnapshot snapshot)
    {
        var aggregate = GitPathState.None;
        foreach (var state in snapshot.States.Values) if (state != GitPathState.Clean) aggregate = HigherPriority(aggregate, state);
        if (aggregate != GitPathState.None) { var d = ToDecoration(aggregate); return new GitDecoration(d.Glyph, $"Git repository: {d.Tooltip.Replace("Git: ", string.Empty)}"); }
        return new GitDecoration("✓", "Git repository: clean");
    }

    private static bool HasDirectGitMarker(string folderPath) { try { var marker = Path.Combine(Path.GetFullPath(folderPath), ".git"); return Directory.Exists(marker) || File.Exists(marker); } catch { return false; } }
    private static bool HasGitMarkerInAncestry(string folderPath)
    {
        try { var current = new DirectoryInfo(Path.GetFullPath(folderPath)); while (current is not null) { var marker=Path.Combine(current.FullName,".git"); if(Directory.Exists(marker)||File.Exists(marker))return true; current=current.Parent; } } catch { }
        return false;
    }
    private static void MergeState(Dictionary<string, GitPathState> states, string path, GitPathState incoming) { if (!states.TryGetValue(path, out var current)) states[path]=incoming; else states[path]=HigherPriority(current,incoming); }
    private static GitPathState HigherPriority(GitPathState a, GitPathState b) => Priority(b)>Priority(a)?b:a;
    private static int Priority(GitPathState s)=>s switch{GitPathState.Deleted=>4,GitPathState.Modified=>3,GitPathState.Added=>2,GitPathState.Clean=>1,_=>0};
    private static GitDecoration ToDecoration(GitPathState s)=>s switch{GitPathState.Clean=>new("✓","Git: tracked / clean"),GitPathState.Modified=>new("!","Git: modified"),GitPathState.Added=>new("+","Git: added / untracked"),GitPathState.Deleted=>new("−","Git: deleted"),_=>GitDecoration.None};
    private static string Normalize(string path)=>Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);

    private static GitCommandResult RunGit(string workingDirectory, string arguments, CancellationToken cancellationToken)
    {
        using var process=new Process{StartInfo=new ProcessStartInfo{FileName="git.exe",Arguments=$"-C \"{workingDirectory}\" {arguments}",UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true}};
        try { if(!process.Start()) return GitCommandResult.Failed; } catch { return GitCommandResult.Failed; }
        var outputTask=process.StandardOutput.ReadToEndAsync(); var errorTask=process.StandardError.ReadToEndAsync(); var sw=Stopwatch.StartNew();
        while(!process.WaitForExit(100)){if(cancellationToken.IsCancellationRequested||sw.ElapsedMilliseconds>=CommandTimeoutMs){try{process.Kill(true);}catch{} cancellationToken.ThrowIfCancellationRequested();return GitCommandResult.Failed;}}
        Task.WaitAll(new Task[]{outputTask,errorTask},500); var output=outputTask.IsCompletedSuccessfully?outputTask.Result:string.Empty; return process.ExitCode==0?new GitCommandResult(true,output):GitCommandResult.Failed;
    }
    private readonly record struct CacheEntry(DateTime ExpiresUtc, GitFolderSnapshot Snapshot);
    private readonly record struct GitCommandResult(bool Success, string Output){internal static GitCommandResult Failed=>new(false,string.Empty);}
}
