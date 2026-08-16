using System.Xml.Linq;
using AIEngineeringWorkspace.FileManager;
using AIEngineeringWorkspace.Workspace;

var failures = new List<string>();
Run("AutoFit_12Panes_NoOverlap", TestAutoFit12PanesNoOverlap);
Run("AutoFit_SinglePane_FillsViewport", TestSinglePaneFill);
Run("GitPorcelain_Rename_UsesDestinationPath", TestRenameUsesDestination);
Run("GitBadge_Xaml_CollapsesEmptyGlyph", TestGitBadgeCollapsed);
Run("Version_SingleSource_IsRc10", TestVersionSource);
Run("WorkspaceProject_RoundTrip_PreservesLayoutAndFilePath", TestWorkspaceProjectRoundTrip);
Run("EndpointBadge_ShowIds_Uses64x64Overlay", TestEndpointBadgeSize);

if (failures.Count == 0)
{
    Console.WriteLine("All regression checks PASS.");
    return 0;
}

Console.Error.WriteLine($"{failures.Count} regression check(s) FAILED:");
foreach (var failure in failures)
{
    Console.Error.WriteLine($"- {failure}");
}
return 1;

void Run(string name, Action test)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failures.Add($"{name}: {ex.Message}");
        Console.Error.WriteLine($"FAIL {name}: {ex.Message}");
    }
}

void TestAutoFit12PanesNoOverlap()
{
    var panes = Enumerable.Range(0, 12).Select(_ => new AutoFitPaneSpec(360, 260)).ToArray();
    var plan = AutoFitLayoutPlanner.Plan(1280, 720, panes, 8, 4);
    Assert(plan.Cells.Count == 12, "expected 12 cells");
    Assert(plan.RequiresScrolling, "12 browser panes at minimum size should require scrolling at 1280x720");
    for (var i = 0; i < plan.Cells.Count; i++)
    {
        for (var j = i + 1; j < plan.Cells.Count; j++)
        {
            Assert(!Overlaps(plan.Cells[i], plan.Cells[j]), $"cells {i} and {j} overlap");
        }
    }
}

void TestSinglePaneFill()
{
    var plan = AutoFitLayoutPlanner.Plan(1280, 720, new[] { new AutoFitPaneSpec(360, 260) }, 8, 4);
    var cell = plan.Cells.Single();
    Assert(Math.Abs(cell.X) < 0.001 && Math.Abs(cell.Y) < 0.001, "single pane must start at origin");
    Assert(cell.Width >= 1280 && cell.Height >= 720, "single pane must fill viewport");
}

void TestRenameUsesDestination()
{
    var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "aiew-tests-repo"));
    var raw = "R  new.txt\0old.txt\0";
    var parsed = GitPorcelainParser.Parse(root, raw);
    var newPath = Path.GetFullPath(Path.Combine(root, "new.txt")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    var oldPath = Path.GetFullPath(Path.Combine(root, "old.txt")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    Assert(parsed.ContainsKey(newPath), "rename destination/current path missing");
    Assert(!parsed.ContainsKey(oldPath), "rename source/old path must not receive visible status");
}

void TestGitBadgeCollapsed()
{
    var root = FindRepositoryRoot();
    var xaml = File.ReadAllText(Path.Combine(root, "src", "AIEngineeringWorkspace.App", "Controls", "FilePane.xaml"));
    Assert(xaml.Contains("DataTrigger Binding=\"{Binding GitGlyph}\" Value=\"\"", StringComparison.Ordinal), "empty GitGlyph collapse trigger missing");
    Assert(xaml.Contains("<Setter Property=\"Visibility\" Value=\"Collapsed\"", StringComparison.Ordinal), "collapsed visibility setter missing");
}

void TestVersionSource()
{
    var root = FindRepositoryRoot();
    var propsPath = Path.Combine(root, "Directory.Build.props");
    var doc = XDocument.Load(propsPath);
    var values = doc.Descendants().ToDictionary(x => x.Name.LocalName, x => x.Value, StringComparer.OrdinalIgnoreCase);
    Assert(values["WorkspaceVersionLabel"] == "v0.0.6rc10", "WorkspaceVersionLabel drift");
    Assert(values["Version"] == "0.0.6-rc10", "package Version drift");
    Assert(values["FileVersion"] == "0.0.6.10", "FileVersion drift");
}

void TestWorkspaceProjectRoundTrip()
{
    var path = Path.Combine(Path.GetTempPath(), $"aiew-{Guid.NewGuid():N}.aew");
    try
    {
        var paneId = Guid.NewGuid();
        var project = new WorkspaceProjectDocument
        {
            ApplicationVersion = "v0.0.6rc10",
            LayoutMode = WorkspaceLayoutMode.FreeLayout,
            ShowEndpointIds = true,
            Panes = new List<WorkspacePaneState>
            {
                new()
                {
                    Kind = PaneKind.File,
                    PaneId = paneId,
                    DisplayIndex = 2,
                    X = 123,
                    Y = 45,
                    Width = 456,
                    Height = 321,
                    FilePath = @"C:\Example"
                },
                new()
                {
                    Kind = PaneKind.Browser,
                    PaneId = Guid.NewGuid(),
                    DisplayIndex = 1,
                    X = 600,
                    Y = 45,
                    Width = 720,
                    Height = 540
                }
            }
        };

        WorkspaceProjectService.Save(path, project);
        var loaded = WorkspaceProjectService.Load(path);
        Assert(loaded.LayoutMode == WorkspaceLayoutMode.FreeLayout, "layout mode not preserved");
        Assert(loaded.ShowEndpointIds, "Show IDs state not preserved");
        var file = loaded.Panes.Single(p => p.Kind == PaneKind.File);
        Assert(file.PaneId == paneId && file.DisplayIndex == 2, "File endpoint identity not preserved");
        Assert(file.X == 123 && file.Y == 45 && file.Width == 456 && file.Height == 321, "pane geometry not preserved");
        Assert(file.FilePath == @"C:\Example", "File path not preserved");
        Assert(typeof(WorkspacePaneState).GetProperty("BrowserUrl") is null, "Browser URL must not be persisted by Workspace project schema");
    }
    finally
    {
        if (File.Exists(path)) File.Delete(path);
        if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
    }
}

void TestEndpointBadgeSize()
{
    var root = FindRepositoryRoot();
    foreach (var file in new[] { "BrowserTile.xaml", "FilePane.xaml" })
    {
        var xaml = File.ReadAllText(Path.Combine(root, "src", "AIEngineeringWorkspace.App", "Controls", file));
        Assert(xaml.Contains("x:Name=\"EndpointOverlayBadgeBorder\"", StringComparison.Ordinal), $"{file} large endpoint badge missing");
        Assert(xaml.Contains("Width=\"64\"", StringComparison.Ordinal) && xaml.Contains("Height=\"64\"", StringComparison.Ordinal), $"{file} endpoint badge is not 64x64");
    }
}

string FindRepositoryRoot()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null)
    {
        if (File.Exists(Path.Combine(current.FullName, "Directory.Build.props")))
        {
            return current.FullName;
        }
        current = current.Parent;
    }
    throw new InvalidOperationException("repository root not found");
}

bool Overlaps(AutoFitCell a, AutoFitCell b)
    => a.X < b.X + b.Width && a.X + a.Width > b.X && a.Y < b.Y + b.Height && a.Y + a.Height > b.Y;

void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
