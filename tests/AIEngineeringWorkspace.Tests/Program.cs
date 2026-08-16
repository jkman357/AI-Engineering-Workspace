using System.Xml.Linq;
using AIEngineeringWorkspace.FileManager;
using AIEngineeringWorkspace.Workspace;

var failures = new List<string>();
Run("AutoFit_12Panes_NoOverlap", TestAutoFit12PanesNoOverlap);
Run("AutoFit_SinglePane_FillsViewport", TestSinglePaneFill);
Run("GitPorcelain_Rename_UsesDestinationPath", TestRenameUsesDestination);
Run("GitBadge_Xaml_CollapsesEmptyGlyph", TestGitBadgeCollapsed);
Run("Version_SingleSource_IsRc14", TestVersionSource);
Run("WorkspaceProject_RoundTrip_PreservesLayoutAndFilePath", TestWorkspaceProjectRoundTrip);
Run("EndpointBadge_ShowIds_Uses64x64Header", TestEndpointBadgeSize);
Run("Version_DisplaySuppressesSourceRevision", TestVersionDisplaySuppressesSourceRevision);
Run("NewWorkspace_RepeatedResetHasVisibleFeedback", TestNewWorkspaceFeedback);
Run("BrowserInput_UsesTransactionalRootFocusHandoff", TestBrowserInputHandoff);
Run("BrowserInput_NoChildHwndFocusGuessing", TestNoChildHwndFocusGuessing);
Run("BrowserInput_MultiPaneManualPassGate_Documented", TestMultiPanePassGateDocumented);
Run("BrowserInput_IMEInstrumentation_IsReadOnlyAndComplete", TestImeInstrumentation);
Run("NewWorkspace_AlwaysConfirmsBeforeReset", TestNewWorkspaceConfirmation);

if (failures.Count == 0)
{
    Console.WriteLine("All regression checks PASS.");
    return 0;
}
Console.Error.WriteLine($"{failures.Count} regression check(s) FAILED:");
foreach (var failure in failures) Console.Error.WriteLine($"- {failure}");
return 1;

void Run(string name, Action test)
{
    try { test(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failures.Add($"{name}: {ex.Message}"); Console.Error.WriteLine($"FAIL {name}: {ex.Message}"); }
}

void TestAutoFit12PanesNoOverlap()
{
    var panes = Enumerable.Range(0, 12).Select(_ => new AutoFitPaneSpec(360, 260)).ToArray();
    var plan = AutoFitLayoutPlanner.Plan(1280, 720, panes, 8, 4);
    Assert(plan.Cells.Count == 12, "expected 12 cells");
    Assert(plan.RequiresScrolling, "12 browser panes at minimum size should require scrolling at 1280x720");
    for (var i=0;i<plan.Cells.Count;i++) for(var j=i+1;j<plan.Cells.Count;j++) Assert(!Overlaps(plan.Cells[i], plan.Cells[j]), $"cells {i} and {j} overlap");
}
void TestSinglePaneFill(){var plan=AutoFitLayoutPlanner.Plan(1280,720,new[]{new AutoFitPaneSpec(360,260)},8,4);var cell=plan.Cells.Single();Assert(Math.Abs(cell.X)<.001&&Math.Abs(cell.Y)<.001,"single pane must start at origin");Assert(cell.Width>=1280&&cell.Height>=720,"single pane must fill viewport");}
void TestRenameUsesDestination(){var root=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"aiew-tests-repo"));var parsed=GitPorcelainParser.Parse(root,"R  new.txt\0old.txt\0");var newPath=Path.GetFullPath(Path.Combine(root,"new.txt")).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);var oldPath=Path.GetFullPath(Path.Combine(root,"old.txt")).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);Assert(parsed.ContainsKey(newPath),"rename destination/current path missing");Assert(!parsed.ContainsKey(oldPath),"rename source/old path must not receive visible status");}
void TestGitBadgeCollapsed(){var root=FindRepositoryRoot();var xaml=File.ReadAllText(Path.Combine(root,"src","AIEngineeringWorkspace.App","Controls","FilePane.xaml"));Assert(xaml.Contains("DataTrigger Binding=\"{Binding GitGlyph}\" Value=\"\"",StringComparison.Ordinal),"empty GitGlyph collapse trigger missing");Assert(xaml.Contains("<Setter Property=\"Visibility\" Value=\"Collapsed\"",StringComparison.Ordinal),"collapsed visibility setter missing");}
void TestVersionSource(){var doc=XDocument.Load(Path.Combine(FindRepositoryRoot(),"Directory.Build.props"));var values=doc.Descendants().ToDictionary(x=>x.Name.LocalName,x=>x.Value,StringComparer.OrdinalIgnoreCase);Assert(values["WorkspaceVersionLabel"]=="v0.0.6rc14","WorkspaceVersionLabel drift");Assert(values["Version"]=="0.0.6-rc14","package Version drift");Assert(values["FileVersion"]=="0.0.6.14","FileVersion drift");}
void TestWorkspaceProjectRoundTrip()
{
    var path=Path.Combine(Path.GetTempPath(),$"aiew-{Guid.NewGuid():N}.aew");
    try
    {
        var paneId=Guid.NewGuid();var project=new WorkspaceProjectDocument{ApplicationVersion="v0.0.6rc14",LayoutMode=WorkspaceLayoutMode.FreeLayout,ShowEndpointIds=true,Panes=new List<WorkspacePaneState>{new(){Kind=PaneKind.File,PaneId=paneId,DisplayIndex=2,X=123,Y=45,Width=456,Height=321,FilePath=@"C:\Example"},new(){Kind=PaneKind.Browser,PaneId=Guid.NewGuid(),DisplayIndex=1,X=600,Y=45,Width=720,Height=540}}};
        WorkspaceProjectService.Save(path,project);var loaded=WorkspaceProjectService.Load(path);Assert(loaded.LayoutMode==WorkspaceLayoutMode.FreeLayout,"layout mode not preserved");Assert(loaded.ShowEndpointIds,"Show IDs state not preserved");var file=loaded.Panes.Single(p=>p.Kind==PaneKind.File);Assert(file.PaneId==paneId&&file.DisplayIndex==2,"File endpoint identity not preserved");Assert(file.X==123&&file.Y==45&&file.Width==456&&file.Height==321,"pane geometry not preserved");Assert(file.FilePath==@"C:\Example","File path not preserved");Assert(typeof(WorkspacePaneState).GetProperty("BrowserUrl") is null,"Browser URL must not be persisted by Workspace project schema");
    }
    finally{if(File.Exists(path))File.Delete(path);if(File.Exists(path+".tmp"))File.Delete(path+".tmp");}
}
void TestEndpointBadgeSize(){var root=FindRepositoryRoot();foreach(var file in new[]{"BrowserTile.xaml","FilePane.xaml"}){var xaml=File.ReadAllText(Path.Combine(root,"src","AIEngineeringWorkspace.App","Controls",file));Assert(xaml.Contains("x:Name=\"EndpointLargeBadgeBorder\"",StringComparison.Ordinal),$"{file} large endpoint header badge missing");Assert(xaml.Contains("Width=\"64\"",StringComparison.Ordinal)&&xaml.Contains("Height=\"64\"",StringComparison.Ordinal),$"{file} endpoint badge is not 64x64");Assert(xaml.Contains("x:Name=\"PaneFrameBorder\"",StringComparison.Ordinal),$"{file} highlighted pane frame missing");}}
void TestVersionDisplaySuppressesSourceRevision(){var root=FindRepositoryRoot();var props=XDocument.Load(Path.Combine(root,"Directory.Build.props"));var values=props.Descendants().ToDictionary(x=>x.Name.LocalName,x=>x.Value,StringComparer.OrdinalIgnoreCase);Assert(values.TryGetValue("IncludeSourceRevisionInInformationalVersion",out var include)&&string.Equals(include,"false",StringComparison.OrdinalIgnoreCase),"source revision suffix suppression missing");var appInfo=File.ReadAllText(Path.Combine(root,"src","AIEngineeringWorkspace.App","Infrastructure","AppInfo.cs"));Assert(appInfo.Contains("IndexOf('+')",StringComparison.Ordinal),"AppInfo defensive '+' metadata stripping missing");}
void TestNewWorkspaceFeedback(){var main=File.ReadAllText(Path.Combine(FindRepositoryRoot(),"src","AIEngineeringWorkspace.App","MainWindow.xaml.cs"));Assert(main.Contains("ResetWorkspaceToDefaults()",StringComparison.Ordinal),"New Workspace reset helper missing");Assert(main.Contains("_newWorkspaceResetCount++",StringComparison.Ordinal),"repeated New Workspace reset sequence feedback missing");Assert(main.Contains("New Workspace project created at",StringComparison.Ordinal),"visible New Workspace timestamp feedback missing");}
void TestBrowserInputHandoff()
{
    var root=FindRepositoryRoot();var host=File.ReadAllText(Path.Combine(root,"src","AIEngineeringWorkspace.App","Controls","BrowserDockHost.cs"));var coordinator=File.ReadAllText(Path.Combine(root,"src","AIEngineeringWorkspace.App","Browser","FirefoxInputCoordinator.cs"));
    Assert(host.Contains("FirefoxInputCoordinator.FocusRoot",StringComparison.Ordinal),"BrowserDockHost does not delegate focus handoff to central coordinator");
    Assert(!host.Contains("AttachDockInputQueues",StringComparison.Ordinal),"persistent per-dock input bridge helper still exists");
    Assert(!host.Contains("_inputQueuesAttached",StringComparison.Ordinal),"persistent bridge state still exists in BrowserDockHost");
    Assert(coordinator.Contains("AttachThreadInput(workspaceThreadId, browserThreadId, true)",StringComparison.Ordinal),"temporary AttachThreadInput attach missing");
    Assert(coordinator.Contains("AttachThreadInput(workspaceThreadId, browserThreadId, false)",StringComparison.Ordinal),"temporary AttachThreadInput detach missing");
    Assert(coordinator.Contains("finally",StringComparison.Ordinal),"input bridge detach is not guarded by finally");
    Assert(coordinator.Contains("SetFocus(browserHwnd)",StringComparison.Ordinal),"Firefox root SetFocus handoff missing");
    Assert(coordinator.Contains("TemporaryInputBridgeAttached",StringComparison.Ordinal)&&coordinator.Contains("TemporaryInputBridgeDetached",StringComparison.Ordinal),"transaction bridge diagnostics missing");
}
void TestNoChildHwndFocusGuessing(){var root=FindRepositoryRoot();var host=File.ReadAllText(Path.Combine(root,"src","AIEngineeringWorkspace.App","Controls","BrowserDockHost.cs"));var coordinator=File.ReadAllText(Path.Combine(root,"src","AIEngineeringWorkspace.App","Browser","FirefoxInputCoordinator.cs"));Assert(!host.Contains("FindPreferredContentHwnd",StringComparison.Ordinal)&&!coordinator.Contains("FindPreferredContentHwnd",StringComparison.Ordinal),"Firefox child-HWND guessing returned");Assert(!host.Contains("EnumChildWindows",StringComparison.Ordinal)&&!coordinator.Contains("EnumChildWindows",StringComparison.Ordinal),"normal Browser focus path enumerates Firefox child HWNDs");}
void TestMultiPanePassGateDocumented(){var note=File.ReadAllText(Path.Combine(FindRepositoryRoot(),"docs","releases","v0.0.6rc14.md"));foreach(var token in new[]{"English/number","Zhuyin","WorkspaceHKL","BrowserHKL","GuiFocus","runtime"})Assert(note.Contains(token,StringComparison.OrdinalIgnoreCase),$"rc14 manual IME diagnostic gate missing token '{token}'");}
void TestNewWorkspaceConfirmation(){var main=File.ReadAllText(Path.Combine(FindRepositoryRoot(),"src","AIEngineeringWorkspace.App","MainWindow.xaml.cs"));Assert(main.Contains("ConfirmCreateNewWorkspace()",StringComparison.Ordinal),"New Workspace explicit confirmation helper missing");Assert(main.Contains("Create a new Workspace project?",StringComparison.Ordinal),"clean Workspace confirmation prompt missing");Assert(main.Contains("Save changes before creating a new Workspace?",StringComparison.Ordinal),"dirty Workspace Save/Discard/Cancel prompt missing");}

void TestImeInstrumentation()
{
    var root=FindRepositoryRoot();
    var native=File.ReadAllText(Path.Combine(root,"src","AIEngineeringWorkspace.App","Interop","NativeMethods.cs"));
    var coordinator=File.ReadAllText(Path.Combine(root,"src","AIEngineeringWorkspace.App","Browser","FirefoxInputCoordinator.cs"));
    var diag=File.ReadAllText(Path.Combine(root,"src","AIEngineeringWorkspace.App","Browser","InputLanguageDiagnostics.cs"));
    var host=File.ReadAllText(Path.Combine(root,"src","AIEngineeringWorkspace.App","Controls","BrowserDockHost.cs"));
    var main=File.ReadAllText(Path.Combine(root,"src","AIEngineeringWorkspace.App","MainWindow.xaml.cs"));
    Assert(native.Contains("GetKeyboardLayout",StringComparison.Ordinal),"GetKeyboardLayout diagnostic P/Invoke missing");
    Assert(native.Contains("GetGUIThreadInfo",StringComparison.Ordinal),"GetGUIThreadInfo diagnostic P/Invoke missing");
    foreach(var token in new[]{"WM_INPUTLANGCHANGEREQUEST","WM_INPUTLANGCHANGE","WM_IME_SETCONTEXT","WM_IME_STARTCOMPOSITION","WM_IME_COMPOSITION","WM_IME_ENDCOMPOSITION"}) Assert(native.Contains(token,StringComparison.Ordinal)&&diag.Contains(token,StringComparison.Ordinal),$"IME/input-language diagnostic message missing: {token}");
    Assert(coordinator.Contains("Firefox input-state transition observed",StringComparison.Ordinal),"periodic Firefox input-state transition diagnostic missing");
    Assert(coordinator.Contains("WorkspaceHKL",StringComparison.Ordinal)&&coordinator.Contains("BrowserHKL",StringComparison.Ordinal)&&coordinator.Contains("GuiFocus",StringComparison.Ordinal),"HKL/GUI-thread evidence missing");
    Assert(host.Contains("InputLanguageDiagnostics.LogWindowMessage(\"BrowserDockHost\"",StringComparison.Ordinal),"HwndHost message diagnostics missing");
    Assert(main.Contains("InstallInputMessageDiagnostics",StringComparison.Ordinal)&&main.Contains("InputLanguageDiagnostics.LogWindowMessage(\"WPF.MainWindow\"",StringComparison.Ordinal),"WPF top-level message diagnostics missing");
    Assert(!coordinator.Contains("WM_IME_COMPOSITION",StringComparison.Ordinal),"coordinator must not synthesize IME composition messages");
    Assert(!coordinator.Contains("ActivateKeyboardLayout",StringComparison.Ordinal)&&!coordinator.Contains("PostMessage",StringComparison.Ordinal),"rc14 must not force/synchronize input layout before evidence is captured");
}

string FindRepositoryRoot(){var current=new DirectoryInfo(AppContext.BaseDirectory);while(current is not null){if(File.Exists(Path.Combine(current.FullName,"Directory.Build.props")))return current.FullName;current=current.Parent;}throw new InvalidOperationException("repository root not found");}
bool Overlaps(AutoFitCell a,AutoFitCell b)=>a.X<b.X+b.Width&&a.X+a.Width>b.X&&a.Y<b.Y+b.Height&&a.Y+a.Height>b.Y;
void Assert(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
