using System.Xml.Linq;
using AIEngineeringWorkspace.FileManager;
using AIEngineeringWorkspace.Workspace;

var failures = new List<string>();
Run("AutoFit_12Panes_NoOverlap", TestAutoFit12PanesNoOverlap);
Run("AutoFit_SinglePane_FillsViewport", TestSinglePaneFill);
Run("GitPorcelain_Rename_UsesDestinationPath", TestRenameUsesDestination);
Run("GitBadge_Xaml_CollapsesEmptyGlyph", TestGitBadgeCollapsed);
Run("Version_SingleSource_IsRc20", TestVersionSource);
Run("WorkspaceProject_RoundTrip_PreservesLayoutAndFilePath", TestWorkspaceProjectRoundTrip);
Run("EndpointBadge_ShowIds_Uses64x64Header", TestEndpointBadgeSize);
Run("Version_DisplaySuppressesSourceRevision", TestVersionDisplaySuppressesSourceRevision);
Run("NewWorkspace_RepeatedResetHasVisibleFeedback", TestNewWorkspaceFeedback);
Run("NewWorkspace_AlwaysConfirmsBeforeReset", TestNewWorkspaceConfirmation);
Run("BrowserInput_NormalPath_IsZeroMutationTopLevelPseudoDock", TestTopLevelPseudoDock);
Run("BrowserInput_ExplicitRecovery_UsesTopLevelActivation", TestTopLevelActivationRecovery);
Run("BrowserInput_NoChildHwndFocusGuessing", TestNoChildHwndFocusGuessing);
Run("BrowserInput_WpfChrome_IsNonFocusable", TestNonFocusableChrome);
Run("BrowserInput_LayoutTransitions_DoNotForceFocus", TestLayoutDoesNotForceFocus);
Run("BrowserInput_IMEInstrumentation_IsDiagnosticOnly", TestImeDiagnosticOnly);
Run("BrowserInput_Rc20ManualPassGate_Documented", TestRc20ManualPassGateDocumented);

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
void TestVersionSource(){var doc=XDocument.Load(Path.Combine(FindRepositoryRoot(),"Directory.Build.props"));var values=doc.Descendants().ToDictionary(x=>x.Name.LocalName,x=>x.Value,StringComparer.OrdinalIgnoreCase);Assert(values["WorkspaceVersionLabel"]=="v0.0.6rc20","WorkspaceVersionLabel drift");Assert(values["Version"]=="0.0.6-rc20","package Version drift");Assert(values["FileVersion"]=="0.0.6.20","FileVersion drift");}
void TestWorkspaceProjectRoundTrip()
{
    var path=Path.Combine(Path.GetTempPath(),$"aiew-{Guid.NewGuid():N}.aew");
    try
    {
        var paneId=Guid.NewGuid();var project=new WorkspaceProjectDocument{ApplicationVersion="v0.0.6rc20",LayoutMode=WorkspaceLayoutMode.FreeLayout,ShowEndpointIds=true,Panes=new List<WorkspacePaneState>{new(){Kind=PaneKind.File,PaneId=paneId,DisplayIndex=2,X=123,Y=45,Width=456,Height=321,FilePath=@"C:\Example"},new(){Kind=PaneKind.Browser,PaneId=Guid.NewGuid(),DisplayIndex=1,X=600,Y=45,Width=720,Height=540}}};
        WorkspaceProjectService.Save(path,project);var loaded=WorkspaceProjectService.Load(path);Assert(loaded.LayoutMode==WorkspaceLayoutMode.FreeLayout,"layout mode not preserved");Assert(loaded.ShowEndpointIds,"Show IDs state not preserved");var file=loaded.Panes.Single(p=>p.Kind==PaneKind.File);Assert(file.PaneId==paneId&&file.DisplayIndex==2,"File endpoint identity not preserved");Assert(file.X==123&&file.Y==45&&file.Width==456&&file.Height==321,"pane geometry not preserved");Assert(file.FilePath==@"C:\Example","File path not preserved");Assert(typeof(WorkspacePaneState).GetProperty("BrowserUrl") is null,"Browser URL must not be persisted by Workspace project schema");
    }
    finally{if(File.Exists(path))File.Delete(path);if(File.Exists(path+".tmp"))File.Delete(path+".tmp");}
}
void TestEndpointBadgeSize(){var root=FindRepositoryRoot();foreach(var file in new[]{"BrowserTile.xaml","FilePane.xaml"}){var xaml=File.ReadAllText(Path.Combine(root,"src","AIEngineeringWorkspace.App","Controls",file));Assert(xaml.Contains("x:Name=\"EndpointLargeBadgeBorder\"",StringComparison.Ordinal),$"{file} large endpoint header badge missing");Assert(xaml.Contains("Width=\"64\"",StringComparison.Ordinal)&&xaml.Contains("Height=\"64\"",StringComparison.Ordinal),$"{file} endpoint badge is not 64x64");Assert(xaml.Contains("x:Name=\"PaneFrameBorder\"",StringComparison.Ordinal),$"{file} highlighted pane frame missing");}}
void TestVersionDisplaySuppressesSourceRevision(){var root=FindRepositoryRoot();var props=XDocument.Load(Path.Combine(root,"Directory.Build.props"));var values=props.Descendants().ToDictionary(x=>x.Name.LocalName,x=>x.Value,StringComparer.OrdinalIgnoreCase);Assert(values.TryGetValue("IncludeSourceRevisionInInformationalVersion",out var include)&&string.Equals(include,"false",StringComparison.OrdinalIgnoreCase),"source revision suffix suppression missing");var appInfo=File.ReadAllText(Path.Combine(root,"src","AIEngineeringWorkspace.App","Infrastructure","AppInfo.cs"));Assert(appInfo.Contains("IndexOf('+')",StringComparison.Ordinal),"AppInfo defensive '+' metadata stripping missing");}
void TestNewWorkspaceFeedback(){var main=File.ReadAllText(Path.Combine(FindRepositoryRoot(),"src","AIEngineeringWorkspace.App","MainWindow.xaml.cs"));Assert(main.Contains("ResetWorkspaceToDefaults()",StringComparison.Ordinal),"New Workspace reset helper missing");Assert(main.Contains("_newWorkspaceResetCount++",StringComparison.Ordinal),"repeated New Workspace reset sequence feedback missing");Assert(main.Contains("New Workspace project created at",StringComparison.Ordinal),"visible New Workspace timestamp feedback missing");}
void TestNewWorkspaceConfirmation(){var main=File.ReadAllText(Path.Combine(FindRepositoryRoot(),"src","AIEngineeringWorkspace.App","MainWindow.xaml.cs"));Assert(main.Contains("ConfirmCreateNewWorkspace()",StringComparison.Ordinal),"New Workspace explicit confirmation helper missing");Assert(main.Contains("Create a new Workspace project?",StringComparison.Ordinal),"clean Workspace confirmation prompt missing");Assert(main.Contains("Save changes before creating a new Workspace?",StringComparison.Ordinal),"dirty Workspace Save/Discard/Cancel prompt missing");}

void TestTopLevelPseudoDock()
{
    var root=FindRepositoryRoot();
    var host=File.ReadAllText(Path.Combine(root,"src","AIEngineeringWorkspace.App","Controls","BrowserDockHost.cs"));
    var coordinator=File.ReadAllText(Path.Combine(root,"src","AIEngineeringWorkspace.App","Browser","FirefoxInputCoordinator.cs"));
    Assert(host.Contains("zero-mutation",StringComparison.OrdinalIgnoreCase),"zero-mutation pseudo-dock declaration missing");
    Assert(host.Contains("GetWindowRect(_hostHwnd",StringComparison.Ordinal),"pseudo-dock screen-rectangle synchronization missing");
    Assert(host.Contains("SetWindowPos(_browserHwnd, IntPtr.Zero",StringComparison.Ordinal),"native top-level geometry positioning missing");
    Assert(!host.Contains("NativeMethods.SetParent(",StringComparison.Ordinal),"rc20 must not reparent Firefox with SetParent");
    Assert(!host.Contains("NativeMethods.SetWindowLongPtr(browserHwnd, NativeMethods.GWL_HWNDPARENT",StringComparison.Ordinal),"rc20 must not reassign Firefox owner");
    Assert(!host.Contains("NativeMethods.SetWindowLongPtr(browserHwnd, NativeMethods.GWL_STYLE",StringComparison.Ordinal),"rc20 must not mutate Firefox style");
    Assert(!host.Contains("NativeMethods.SetWindowLongPtr(browserHwnd, NativeMethods.GWL_EXSTYLE",StringComparison.Ordinal),"rc20 must not mutate Firefox extended style");
    Assert(!host.Contains("WS_POPUP",StringComparison.Ordinal),"rc20 host should not synthesize a Firefox popup style");
    Assert(coordinator.Contains("NativeInputMode=ZeroMutationTopLevelPseudoDock",StringComparison.Ordinal),"zero-mutation top-level diagnostics missing");
    Assert(coordinator.Contains("OwnerMutation=False",StringComparison.Ordinal)&&coordinator.Contains("StyleMutation=False",StringComparison.Ordinal),"owner/style zero-mutation diagnostics missing");
}

void TestTopLevelActivationRecovery()
{
    var root=FindRepositoryRoot();
    var coordinator=File.ReadAllText(Path.Combine(root,"src","AIEngineeringWorkspace.App","Browser","FirefoxInputCoordinator.cs"));
    var host=File.ReadAllText(Path.Combine(root,"src","AIEngineeringWorkspace.App","Controls","BrowserDockHost.cs"));
    Assert(coordinator.Contains("SetForegroundWindow(browserHwnd)",StringComparison.Ordinal),"explicit native top-level activation recovery missing");
    Assert(coordinator.Contains("AttachThreadInputUsed=False",StringComparison.Ordinal),"activation recovery does not declare AttachThreadInput disabled");
    Assert(coordinator.Contains("SetFocusUsed=False",StringComparison.Ordinal),"activation recovery does not declare SetFocus disabled");
    Assert(!coordinator.Contains("AttachThreadInput(",StringComparison.Ordinal),"rc20 must not bridge input queues");
    Assert(!coordinator.Contains("SetFocus(browserHwnd)",StringComparison.Ordinal),"rc20 must not force Firefox root focus");
    Assert(host.Contains("TabIntoCore",StringComparison.Ordinal)&&host.Contains("FocusBrowser(\"BrowserDockHost.TabIntoCore\")",StringComparison.Ordinal),"TabIntoCore activation recovery path missing");
    Assert(host.Contains("public void FocusBrowser(string reason)",StringComparison.Ordinal),"explicit Focus recovery overload missing");
}

void TestNoChildHwndFocusGuessing(){var root=FindRepositoryRoot();var host=File.ReadAllText(Path.Combine(root,"src","AIEngineeringWorkspace.App","Controls","BrowserDockHost.cs"));var coordinator=File.ReadAllText(Path.Combine(root,"src","AIEngineeringWorkspace.App","Browser","FirefoxInputCoordinator.cs"));Assert(!host.Contains("FindPreferredContentHwnd",StringComparison.Ordinal)&&!coordinator.Contains("FindPreferredContentHwnd",StringComparison.Ordinal),"Firefox child-HWND focus guessing returned");Assert(!host.Contains("EnumChildWindows",StringComparison.Ordinal)&&!coordinator.Contains("EnumChildWindows",StringComparison.Ordinal),"Browser path enumerates Firefox child HWNDs");}

void TestNonFocusableChrome()
{
    var root=FindRepositoryRoot();
    var mainXaml=File.ReadAllText(Path.Combine(root,"src","AIEngineeringWorkspace.App","MainWindow.xaml"));
    var browserXaml=File.ReadAllText(Path.Combine(root,"src","AIEngineeringWorkspace.App","Controls","BrowserTile.xaml"));
    Assert(mainXaml.Contains("<Setter Property=\"Focusable\" Value=\"False\"/>",StringComparison.Ordinal),"Workspace toolbar non-focusable style missing");
    Assert(mainXaml.Contains("<Setter Property=\"KeyboardNavigation.IsTabStop\" Value=\"False\"/>",StringComparison.Ordinal),"Workspace toolbar non-tab-stop style missing");
    foreach(var name in new[]{"MaximizePaneButton","ClosePaneButton","LaunchButton","DockExistingButton","FocusButton","DetachButton"})
        Assert(browserXaml.Contains($"x:Name=\"{name}\" Focusable=\"False\" KeyboardNavigation.IsTabStop=\"False\"",StringComparison.Ordinal),$"Browser chrome {name} can still take keyboard focus");
}

void TestLayoutDoesNotForceFocus()
{
    var main=File.ReadAllText(Path.Combine(FindRepositoryRoot(),"src","AIEngineeringWorkspace.App","MainWindow.xaml.cs"));
    Assert(!main.Contains("ScheduleActiveBrowserFocusAfterLayout",StringComparison.Ordinal),"automatic active Browser focus recovery still exists");
    Assert(!main.Contains("RecoverBrowserFocusAfterLayout",StringComparison.Ordinal),"layout path still calls Browser focus recovery");
    Assert(main.Contains("ScheduleBrowserRepaintAfterLayout",StringComparison.Ordinal),"deferred repaint-only layout path missing");
    Assert(main.Contains("SchedulePseudoDockGeometrySync",StringComparison.Ordinal),"top-level pseudo-dock geometry sync path missing");
    Assert(main.Contains("MainWindowLocationChanged",StringComparison.Ordinal),"window-move pseudo-dock geometry sync missing");
    Assert(main.Contains("WorkspaceScrollChanged",StringComparison.Ordinal),"scroll pseudo-dock geometry sync missing");
}

void TestImeDiagnosticOnly()
{
    var root=FindRepositoryRoot();
    var native=File.ReadAllText(Path.Combine(root,"src","AIEngineeringWorkspace.App","Interop","NativeMethods.cs"));
    var coordinator=File.ReadAllText(Path.Combine(root,"src","AIEngineeringWorkspace.App","Browser","FirefoxInputCoordinator.cs"));
    var diag=File.ReadAllText(Path.Combine(root,"src","AIEngineeringWorkspace.App","Browser","InputLanguageDiagnostics.cs"));
    var main=File.ReadAllText(Path.Combine(root,"src","AIEngineeringWorkspace.App","MainWindow.xaml.cs"));
    Assert(native.Contains("GetKeyboardLayout",StringComparison.Ordinal)&&native.Contains("GetGUIThreadInfo",StringComparison.Ordinal),"HKL/GUI diagnostics missing");
    foreach(var token in new[]{"WM_INPUTLANGCHANGEREQUEST","WM_INPUTLANGCHANGE","WM_IME_SETCONTEXT","WM_IME_STARTCOMPOSITION","WM_IME_COMPOSITION","WM_IME_ENDCOMPOSITION"}) Assert(native.Contains(token,StringComparison.Ordinal)&&diag.Contains(token,StringComparison.Ordinal),$"IME/input-language diagnostic message missing: {token}");
    Assert(coordinator.Contains("Firefox input-language state observed without synchronization",StringComparison.Ordinal),"diagnostic-only HKL observation missing");
    Assert(main.Contains("WPF.MainWindow.WM_INPUTLANGCHANGE.DiagnosticOnly",StringComparison.Ordinal),"MainWindow input-language path is not explicitly diagnostic-only");
    Assert(!coordinator.Contains("PostMessage(",StringComparison.Ordinal),"rc20 must not post input-language changes into Firefox");
    Assert(!coordinator.Contains("ActivateKeyboardLayout",StringComparison.Ordinal),"rc20 must not force keyboard layouts");
    Assert(!coordinator.Contains("WM_IME_COMPOSITION,",StringComparison.Ordinal),"rc20 must not synthesize IME composition");
}

void TestRc20ManualPassGateDocumented()
{
    var note=File.ReadAllText(Path.Combine(FindRepositoryRoot(),"docs","releases","v0.0.6rc20.md"));
    foreach(var token in new[]{"B1","Zhuyin","English","abc123","你好","SetParentUsed=False","OwnerMutation=False","StyleMutation=False","title bar","control"})
        Assert(note.Contains(token,StringComparison.OrdinalIgnoreCase),$"rc20 zero-mutation manual gate missing token '{token}'");
}

string FindRepositoryRoot(){var current=new DirectoryInfo(AppContext.BaseDirectory);while(current is not null){if(File.Exists(Path.Combine(current.FullName,"Directory.Build.props")))return current.FullName;current=current.Parent;}throw new InvalidOperationException("repository root not found");}
bool Overlaps(AutoFitCell a,AutoFitCell b)=>a.X<b.X+b.Width&&a.X+a.Width>b.X&&a.Y<b.Y+b.Height&&a.Y+a.Height>b.Y;
void Assert(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
