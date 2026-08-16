using System.Xml.Linq;
using AIEngineeringWorkspace.FileManager;
using AIEngineeringWorkspace.Workspace;

var failures = new List<string>();
Run("AutoFit_12Panes_NoOverlap", TestAutoFit12PanesNoOverlap);
Run("AutoFit_SinglePane_FillsViewport", TestSinglePaneFill);
Run("GitPorcelain_Rename_UsesDestinationPath", TestRenameUsesDestination);
Run("GitBadge_Xaml_CollapsesEmptyGlyph", TestGitBadgeCollapsed);
Run("Version_SingleSource_IsRc09", TestVersionSource);

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
    Assert(values["WorkspaceVersionLabel"] == "v0.0.6rc09", "WorkspaceVersionLabel drift");
    Assert(values["Version"] == "0.0.6-rc09", "package Version drift");
    Assert(values["FileVersion"] == "0.0.6.9", "FileVersion drift");
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
