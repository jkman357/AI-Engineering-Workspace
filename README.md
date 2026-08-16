# AI Engineering Workspace

Current version: **v0.0.6rc10**

Repository: `jkman357/AI-Engineering-Workspace`

AI Engineering Workspace is a .NET 10 / WPF Windows desktop workspace that docks real Firefox windows together with project-oriented file panes. The current RC is intentionally API-free and is designed for standard-user operation.

## Current architecture direction

```text
Unified Dynamic Workspace
├─ Browser Pane (B1 ... B8)
│  ├─ real Firefox HWND docking
│  ├─ Firefox-native address bar / navigation
│  ├─ Workspace maximize / restore
│  ├─ Launch + Dock / Dock Existing / Focus / Detach
│  └─ per-window lifecycle ownership
├─ File Pane (F1 ... F4)
│  ├─ Windows Shell file/folder icons
│  ├─ native Windows Shell context menu
│  ├─ 7-Zip / compare / TortoiseGit Shell-extension participation
│  ├─ Git working-tree status badges
│  ├─ sortable Name / Type / Size / Modified columns
│  └─ Windows FileDrop drag source
├─ Adaptive Layout
│  ├─ Auto Fit
│  └─ Free Layout
└─ Endpoint Identity
   ├─ stable internal PaneId (GUID)
   ├─ human-readable B#/F# alias
   └─ future context/message-routing target
```

## v0.0.6rc10 — Workspace Project Save/Load + Endpoint UI

This RC continues the active **v0.0.6** line and adds the first persistent Workspace-project format without freezing the release.

### Workspace project files (`.aew`)

The top toolbar now provides New / Open / Save / Save As commands. A Workspace project is a human-readable JSON document using the `.aew` extension.

The project persists:

- Auto Fit or Free Layout mode;
- each pane's stable `PaneId` and human-facing `F#` / `B#` alias;
- pane X/Y position and width/height;
- File-pane current folder path;
- Show IDs state;
- main-window size/state.

Browser history, the currently browsed URL, cookies, session state, passwords, and credentials are **not** part of the Workspace-project schema. Restored Browser panes are reconstructed as endpoints only; `Launch + Dock` continues to start Firefox at `https://www.google.com/` and normal navigation remains Firefox-owned.

If a saved File-pane folder no longer exists when a project is opened, that pane falls back to the user's Desktop and records the fallback in runtime diagnostics. A missing folder does not cause the entire Workspace project to fail loading.

Unsaved pane/layout/path changes mark the window title with `*`. New/Open/Close prompts allow the user to save or discard those changes.

### Endpoint UI cleanup

The duplicate `Files 1` / `Browser 1` title text is no longer the visible endpoint label. The compact boxed alias (`F1..F4` / `B1..B8`) remains visible in each pane header and uses the endpoint color palette.

When `#` / Show IDs is enabled, each pane additionally displays a **64×64** color-coded endpoint badge. The large badge is a human-facing routing aid only; stable internal identity remains `PaneId` plus the explicit alias.

### Regression coverage

The dependency-free test harness now also checks Workspace-project JSON round-trip behavior, confirms Browser URL state is not part of the persisted pane schema, validates rc10 version authority, and checks the 64×64 endpoint overlay markup.

## v0.0.6rc09 — Review Hardening

This RC responds to the six findings from the rc08 source review. It does not freeze `v0.0.6` and intentionally adds no new product feature.

### Auto Fit minimum-size invariant

Auto Fit now uses a minimum-size-aware layout planner. Placement stride and pane size come from the same computed cell geometry, so supported pane counts cannot overlap merely because a Browser/File minimum size is larger than the visible viewport cell. When all panes physically cannot fit, Auto Fit enlarges the Canvas and allows scrolling instead of overlapping panes. A single pane still fills the viewport.

### Non-blocking Git decoration

File/folder enumeration is shown first. Git probing then runs off the WPF dispatcher, is cancellable when navigation changes, uses a short snapshot cache, and applies badges only if the result still belongs to the current navigation generation. Git subprocess waits are cancellation-aware and retain the existing timeout guard.

### Firefox pending-launch ownership

Firefox launch is now treated as a launch transaction inside `FirefoxWindowService`. A pending launch records the pre-launch HWND baseline immediately after `Process.Start`. Workspace shutdown can synchronously claim and clean that pending transaction so a Firefox window created during the discovery gap is not left behind merely because the UI continuation never recorded ownership. Cancellation/failure paths also clean newly created transaction candidates without touching the pre-existing Firefox baseline.

### Correctness / release polish

- Git porcelain rename/copy parsing keeps the first `-z` path as the current/destination path and consumes the following old/source path.
- Empty Git glyphs collapse the overlay instead of drawing a blank badge box.
- `Directory.Build.props` is the executable version authority for package/file/informational version metadata and the command-line build/run/test labels.
- a dependency-free regression project is included under `tests/AIEngineeringWorkspace.Tests/`; `test.cmd` exercises Auto Fit overlap invariants, one-pane fill, Git rename parsing, empty-badge XAML, and version consistency.

## v0.0.6rc08 — Auto-Fit Completion + Native HWND Repaint + Git Status UI

This RC continues the active **v0.0.6** line. It does not freeze `v0.0.6`.

### Auto Fit reflow from the Workspace origin

Auto Fit now treats layout as a deterministic reflow rather than preserving stale Canvas geometry. Panes are rebuilt from stable endpoint identity, the Workspace scroll position is reset to the top-left, and visible panes are fitted again after the WPF render pass. This prevents old Free Layout scroll offsets or pane positions from making a correctly reflowed grid appear to have a blank top-left region.

### Browser repaint hardening

A real Firefox HWND is still hosted inside WPF. During live geometry changes, the host now invalidates the foreign HWND and its child windows. When a resize drag or Auto Fit reflow finishes, the Browser pane performs one final fit + Win32 redraw pass to reduce stale pixels / resize ghosting.

### Firefox owns browser navigation UI

The Workspace Browser URL TextBox has been removed. Firefox already provides its own tab/address/navigation controls, so Browser panes now keep only Workspace-level controls: endpoint identity, Launch + Dock, Dock Existing, Focus recovery, Detach, maximize/restore, move/resize, and close.

`Launch + Dock` still opens Firefox at the Workspace default startup URL (`https://www.google.com/`), but normal browsing and URL entry happen directly in Firefox. This also removes a WPF URL TextBox that could compete for keyboard focus.

### Color-coded endpoint identity

When `#` / Show IDs is enabled, `B1..B8` and `F1..F4` badges use distinct fixed colors for faster visual identification. The color is only a human-facing cue: routing identity remains the stable internal `PaneId` GUID plus the explicit B#/F# alias, never color alone.

## v0.0.6rc06 — Native Shell + Git Status + Resize Reliability

This RC continues the active **v0.0.6** line. It does not freeze `v0.0.6`.

### Reliable Free Layout resize hit frame

Real-machine testing showed that rc05 pane movement worked, but the transparent resize controls were not reliably hit-testable. rc06 changes the eight edge/corner `Thumb` controls to use explicit transparent hit surfaces, raises the resize chrome above pane content, and widens the hit bands.

Free Layout supports direct drag resize from:

- left / right;
- top / bottom;
- all four corners.

The pointer changes to the corresponding Windows resize cursor. File content and the docked Firefox HWND continue to fit the pane while it is resized.

### Native Windows Shell context menu

The File pane no longer substitutes a custom WPF right-click menu for normal Windows Shell behavior. Right-clicking a selected item asks Windows Shell for its native `IContextMenu` and forwards menu messages required by Shell extensions.

This allows commands registered on the workstation to participate without hard-coding each tool, for example:

- Windows Open / Open with / Send to / Properties and other registered verbs;
- 7-Zip extraction/compression commands when the installed 7-Zip Shell extension exposes them;
- compare-tool commands when that application has registered a Shell context-menu extension;
- TortoiseGit commands such as Commit / Diff / Log / Pull / Push when TortoiseGit exposes them for the selected Git item.

The exact menu remains controlled by Windows and the locally installed Shell extensions. AI Engineering Workspace does not implement or emulate those third-party commands.

Keyboard file actions (`Ctrl+C`, `Ctrl+X`, `Ctrl+V`, `F2`, `Delete`) remain available as Workspace fallback operations.

### Git working-tree indicators

When a File pane is inside a Git working tree and `git.exe` is available, rc06 adds a small status badge over the file/folder icon:

```text
✓  tracked / clean
!  modified
+  added or untracked
−  deleted
```

The indicator is derived from local `git status --porcelain` / `git ls-files` output with a short command timeout. TortoiseGit remains responsible for its own Shell context-menu integration; the status badge is a lightweight Workspace fallback and does not modify the repository.

## v0.0.6rc04 — Browser Input Focus + Toolbar UX

`v0.0.6rc04` added a best-effort Win32 focus bridge for the real external Firefox HWND hosted inside WPF.

- the WPF URL TextBox releases keyboard focus when Enter is pressed;
- after `Ctrl+L` + URL + Enter navigation, the host attempts to return Win32 focus to Firefox web content;
- the dock host enumerates Firefox child HWNDs and prefers a visible Mozilla content/compositor surface when available;
- focus diagnostics include root/target HWND, PID/thread, foreground HWND, and focus evidence;
- the Focus command remains a manual recovery/diagnostic action rather than a required step before normal page input;
- Browser actions use compact icon buttons with tooltips: `▶`, `⇲`, `⌖`, `↗`.

## v0.0.6rc03 — Adaptive Workspace + File Manager UX + Security Hardening

This RC continues the **v0.0.6** line. The version is not frozen; later fixes remain `v0.0.6rcXX` until the release is explicitly frozen.

### Adaptive workspace layout

The Workspace now has two layout modes.

**Auto Fit** is the default:

- remaining panes automatically reflow after a pane is added or removed;
- panes are recalculated when the available Workspace viewport changes;
- the active panes fill the available Workspace instead of leaving deleted-pane holes;
- Firefox HWND content is resized with its Browser pane.

**Free Layout** preserves manual geometry:

- panes can be dragged freely with the `⋮⋮` handle;
- panes can be resized directly from any edge or corner;
- dragging a pane onto another pane can exchange their positions;
- manual move/resize automatically changes Auto Fit to Free Layout so the Workspace does not immediately undo the user's manual geometry.

The toolbar layout icon toggles Auto Fit / Free Layout. Hover it for the current-mode tooltip.

### Browser pane maximize / restore

Each Browser pane has a small `□` control.

- `□` maximizes that Browser pane **inside the AI Engineering Workspace**.
- Other panes are temporarily hidden.
- The docked Firefox HWND fills the Browser pane client region.
- `❐` restores the previous Workspace layout.

This is intentionally **not Firefox F11 monitor full-screen mode**. The Workspace retains ownership of pane identity, close controls, status, and future routing UI. (The Workspace URL control described by rc03 was removed in rc07; Firefox now owns normal address/navigation UI.)

Browser panes still default to:

```text
https://www.google.com/
```

Historical note: rc03-rc06 exposed a Workspace URL field. rc07 removes that duplicate control; normal URL entry and navigation are handled directly by Firefox.

### Browser lifecycle safety

- Firefox launch/HWND discovery is serialized to reduce cross-pane assignment races.
- One Firefox HWND cannot be owned by two Browser panes.
- Workspace-launched Firefox windows are tracked by exact HWND + PID.
- Closing a Workspace-owned Browser pane gracefully closes that exact Firefox window.
- A Firefox window attached through `Dock Existing` is detached/restored instead of force-closed.
- Closing the Workspace gracefully closes Firefox windows launched by the Workspace.
- Normal lifecycle handling does not use `Process.Kill()`.

## Dynamic pane identity

Current POC limits:

- Browser panes: `B1..B8`
- File panes: `F1..F4`

Display numbers reuse the smallest free number. For example, deleting `B2` makes `B2` available to the next Browser pane.

Every pane also owns an internal GUID `PaneId`. The display alias is for human operation; future routing should use `PaneId`, so moving a pane does not change its routing identity.

Use the `#` toolbar icon to show/hide `B#` and `F#` routing aliases.

## File Manager

File panes use Windows Shell icons for folders and registered file types.

The default File panes open:

- `F1` → Downloads
- `F2` → Desktop

### Navigation and open

- path entry + Go / Enter
- parent folder
- refresh
- double-click folder navigation
- double-click file open through the registered Windows Shell application
- Windows `FileDrop` drag source for browser upload workflows

### Sortable columns

Click a column header to sort; click it again to reverse the direction:

- `Name ↑/↓`
- `Type ↑/↓`
- `Size ↑/↓`
- `Modified ↑/↓`

Folders remain grouped before files, while the selected column controls ordering inside the groups.

### Native Windows Shell actions

Right-click uses the native Windows Shell context menu for the selected file/folder (or the current folder when no row is selected). Therefore the available commands depend on Windows and locally registered Shell extensions such as 7-Zip, comparison tools, and TortoiseGit.

Keyboard shortcuts provided directly by the Workspace remain:

```text
Ctrl+C  Copy
Ctrl+X  Cut / Move
Ctrl+V  Paste
F2      Rename
Delete  Delete to Recycle Bin
```

Paste does not silently overwrite an existing destination item in this RC. Name collisions are skipped and logged.

## Security and privacy

### Credential handling

AI Engineering Workspace **does not intentionally provide, implement, or maintain credential/password storage**, and the application is not designed to collect or manage user authentication credentials.

The Workspace does not implement its own password vault, credential database, account database, or authentication provider.

Firefox remains responsible for browser-managed authentication state, including cookies, sessions, saved logins, and any password-manager behavior enabled by the user. AI Engineering Workspace does not require reading Firefox saved-login databases as part of its current design and does not intentionally access or maintain browser password-manager data.

This wording describes the application's design intent and functional boundary; it is not an absolute claim that arbitrary third-party, operating-system, browser, exception, or diagnostic data can never contain sensitive text.

See [`SECURITY.md`](SECURITY.md) for trust boundaries and diagnostics details.

## Diagnostics policy

Runtime diagnostics are local engineering logs. They are **not automatically uploaded by the application**.

When the application is run from a source checkout, runtime logs are normally written to:

```text
logs\runtime\AIEngineeringWorkspace_*.log
```

When no repository root can be found, the fallback location is:

```text
%LOCALAPPDATA%\AIEngineeringWorkspace\logs\runtime\
```

Default runtime-log policy in the current `v0.0.6` RC line:

- retention age: **14 days**;
- maximum retained runtime files: **50**;
- rotation: **10 MB per runtime log file**;
- sensitive-value handling: **best-effort redaction** for common password/token/Authorization patterns and sensitive URL query parameters.

Environment variables:

```text
AIEW_RUNTIME_LOG=0          Disable application runtime logging
AIEW_LOG_RETENTION_DAYS=14  Runtime-log retention age
AIEW_LOG_MAX_FILES=50       Maximum retained runtime log files
AIEW_LOG_MAX_MB=10          Per-file rotation threshold in MB
```

Best-effort redaction is **not a security boundary or a guarantee**. Diagnostic logs may still contain information such as URLs, local paths, file names, process IDs, HWND values, pane aliases/PaneIds, and exception context. Review logs before sharing them publicly.

Build and launcher scripts also create local diagnostic logs under `logs\build\` and `logs\runtime\`. Those script-generated files are separate from the application's runtime rotation policy.

## Firefox profile trust boundary

The current architecture treats Firefox and the selected Firefox profile as an external trust boundary:

```text
AI Engineering Workspace
        │
        │ HWND hosting / controlled UI interaction
        ▼
Firefox process
        │
        ▼
Firefox profile
├─ cookies
├─ sessions
├─ saved logins
└─ browser settings
```

The Workspace can control a browser window that may already represent an authenticated session. That capability must be considered when evaluating physical access, workstation access, endpoint security, and future routing/automation features.

## Privilege / IT-policy model

The executable manifest uses `asInvoker` and the application is **designed for standard-user execution**. Administrator privileges are not required by design.

`asInvoker` is not an absolute non-elevation guarantee: if the application is launched from an already elevated parent context, it can inherit that token. The project therefore avoids wording such as “always runs without Administrator privileges.”

Build/run scripts do not use PowerShell execution-policy bypasses and do not intentionally circumvent endpoint-management or IT security policy.

## Build

Development baseline:

- Visual Studio 2026
- C#
- .NET 10
- WPF
- Windows

From a normal Command Prompt:

```bat
build.cmd
```

Build log:

```text
logs\build\build_*.log
```

The project is intended to remain command-line buildable without opening Visual Studio.

## Regression tests

```bat
test.cmd
```

The rc09 test project has no external test-framework package dependency; it is a small executable regression harness so validation remains usable from a clean .NET 10 SDK installation. Test logs are written under `logs\test\`.

## Run

```bat
run.cmd
```

Launcher/application diagnostics are written under `logs\runtime\` when logging is enabled.

## v0.0.6rc08 test focus / reflow / repaint / Shell integration

1. Run `build.cmd` and then `run.cmd`.
2. In Auto Fit, add/remove panes after using Free Layout and scrolling. Verify the remaining panes reflow from the top-left with no unexplained blank origin region.
3. Switch to Free Layout and resize File panes from all four edges and all four corners. The cursor must change and the pane must resize continuously.
4. Repeat resize with a docked Firefox Browser pane. Firefox must continue to fill the Browser client area and should not leave stale/duplicated pixels after resize completes.
5. Verify Browser panes no longer show a Workspace URL TextBox. Use Firefox's own address bar for navigation.
6. Enable `#` / Show IDs. Verify B#/F# badges are color-coded, readable, and remain the same alias while panes move.
7. Right-click a `.zip` file. Verify the native Windows menu appears and, when registered on the machine, 7-Zip commands are present.
8. Navigate a File pane into a Git working tree. Verify Git badges appear and TortoiseGit Shell commands are available when registered.
9. Re-test Browser Launch + Dock, normal page keyboard input, Focus recovery, Detach, pane maximize/restore, and Workspace shutdown lifecycle.
10. If repaint/focus/Shell behavior fails, attach the runtime log and note the pane alias, selected file type (if applicable), and exact action sequence.

## v0.0.6rc10 validation focus

1. Run `build.cmd`, then `test.cmd`; both must return exit code 0 before GUI testing.
2. Arrange panes in Free Layout, change File paths, enable Show IDs, and save a `.aew` project. Close/reopen it and verify pane aliases, `PaneId`-backed identity, geometry, layout mode, File paths, and Show IDs state are restored.
3. Rename or temporarily remove one saved File-pane folder before reopening the project. Only that File pane must fall back to Desktop; the rest of the project must still load.
4. Open a saved project containing Browser panes. The panes must be restored without restoring browsing history/current URL. `Launch + Dock` must start at Google.
5. Verify compact boxed `F#` / `B#` aliases remain visible. Toggle `#` and verify the large endpoint overlay is approximately 64×64 and color-coded.
6. Modify pane position/size/path after saving and confirm the title gains `*`. Verify New/Open/application Close prompts allow Save / Don't Save / Cancel behavior.
7. Re-run rc09 review-hardening checks: Auto Fit must not overlap, Git decoration must remain asynchronous, and pending Firefox launch cleanup must not orphan a Workspace-launched window.
8. Check executable/file/log metadata: rc10 artifacts must identify `v0.0.6rc10` / `0.0.6.10`.

## Current limits / non-goals

This remains an RC-stage engineering POC. The following are not implemented yet:

- cross-chat context/message routing
- controlled clipboard routing between endpoints
- AI APIs
- browser DOM automation
- application-owned credential/password management

## Roadmap direction

```text
Adaptive Dynamic Pane Workspace
        +
Endpoint Identity
        +
File / Browser UX
        +
Workspace Project Save / Load
        ↓
Endpoint Registry
        ↓
Controlled Context / Message Routing
        ↓
Same-model / cross-model chat workflows
```

## License

This project is licensed under the MIT License. See [`LICENSE`](LICENSE).

## Copyright

Copyright (c) 2026 Ray Yang

## Disclaimer

This project is provided as an engineering workspace and reference implementation.

Use in production, enterprise, medical, safety-critical, regulated, or other high-assurance environments requires independent evaluation of security, privacy, reliability, regulatory, compliance, validation, and operational risks by the user or adopting organization.

The project has not been independently validated, certified, or qualified for any specific regulated or safety-critical application.

This README disclaimer is practical risk guidance. It does **not** restrict or modify the permissions granted under the MIT License. The applicable warranty and liability terms are stated in [`LICENSE`](LICENSE).


## v0.0.6rc08 validation focus

This RC closes three issues found during real Windows testing:

- Auto Fit treats a single remaining pane as the whole Workspace and fills the visible client area from the top-left.
- Docked Firefox resize/reflow uses a stronger native repaint sequence (redraw suppression during geometry commit, then browser/host/parent invalidation) to reduce stale foreign-HWND pixels.
- File panes show visible Git repository-root badges even when the current folder is outside a repository; inside a work tree, file/folder badges continue to show clean/modified/added/deleted state using local `git.exe`.

These changes do not add credential storage, browser-profile access, or Git write operations.
