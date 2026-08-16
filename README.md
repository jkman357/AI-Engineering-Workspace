# AI Engineering Workspace

Current version: **v0.0.6rc05**

Repository: `jkman357/AI-Engineering-Workspace`

AI Engineering Workspace is a .NET 10 / WPF Windows desktop workspace that docks real Firefox windows together with project-oriented file panes. The current RC is intentionally API-free and is designed for standard-user operation.

## Current architecture direction

```text
Unified Dynamic Workspace
├─ Browser Pane (B1 ... B8)
│  ├─ real Firefox HWND docking
│  ├─ URL + Enter navigation
│  ├─ Workspace maximize / restore
│  ├─ Launch + Dock / Dock Existing / Focus / Detach
│  └─ per-window lifecycle ownership
├─ File Pane (F1 ... F4)
│  ├─ Windows Shell file/folder icons
│  ├─ Explorer-like file actions
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

## v0.0.6rc05 — Free Layout Border Resize + Focus P/Invoke Fix

This RC continues the active **v0.0.6** line and fixes issues found during real-machine testing of `v0.0.6rc04`. The release is not frozen.

### True Free Layout border resize

Free Layout pane resizing is no longer limited to the small lower-right `◢` grip.

Each File/Browser pane now supports direct resize from:

- left / right edges;
- top / bottom edges;
- top-left / top-right corners;
- bottom-left / bottom-right corners.

Standard WPF resize cursors are used for each direction. Browser/File content is inset slightly so a WPF-owned resize band remains available around the pane; this is important for Browser panes because the embedded Firefox HWND is a foreign native window and can otherwise consume pointer input over its client region.

Resizing from the left or top moves the pane origin while keeping the opposite edge stable. Minimum pane size and non-negative top/left workspace coordinates are enforced. A docked Firefox HWND continues to follow the Browser pane size.

### Win32 focus bridge correction

`v0.0.6rc04` introduced richer Firefox focus diagnostics, but real-machine testing exposed an incorrect P/Invoke declaration:

```text
GetCurrentThreadId
```

The function is now imported from `kernel32.dll` instead of `user32.dll`. This removes the observed `Unable to find an entry point named 'GetCurrentThreadId' in DLL 'user32.dll'` Launch + Dock failure.

The Browser input-focus bridge and compact icon toolbar from rc04 are retained.

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

This is intentionally **not Firefox F11 monitor full-screen mode**. The Workspace retains ownership of pane identity, URL controls, close controls, status, and future routing UI.

Browser panes still default to:

```text
https://www.google.com/
```

Entering a URL and pressing Enter navigates only that Browser endpoint. If no browser is docked yet, Enter launches and docks Firefox using that URL.

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

### Explorer-like actions

Right-click provides:

- Open
- Copy
- Cut / Move
- Paste
- Rename
- Delete to Recycle Bin
- New Folder
- Refresh

Keyboard shortcuts:

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

## Run

```bat
run.cmd
```

Launcher/application diagnostics are written under `logs\runtime\` when logging is enabled.

## v0.0.6rc05 test focus / resize

1. Run `build.cmd` and then `run.cmd`.
2. Launch + Dock Firefox in `B1`; the previous `GetCurrentThreadId` entry-point error must not occur.
3. Switch to Free Layout, or start a border resize while Auto Fit is active and verify the Workspace switches to Free Layout.
4. Resize a File pane from left, right, top, bottom, and all four corners.
5. Repeat the same eight-direction resize on a Browser pane with Firefox docked.
6. Verify the cursor changes to horizontal, vertical, or diagonal resize cursors at the appropriate border/corner.
7. Verify the docked Firefox client region follows the Browser pane continuously and remains usable after resize.
8. Verify left/top resize changes the pane origin without allowing negative workspace coordinates or violating minimum pane size.
9. Open ChatGPT (or another page with a normal text input), click the page input, and type without pressing the Workspace Focus button first.
10. Enter `www.yahoo.com.tw` in the Workspace URL field and press Enter; only that Browser endpoint should navigate.
11. Exercise `▶`, `⇲`, `⌖`, and `↗`; verify each tooltip matches its action.
12. Re-test Browser maximize/restore, Detach, Auto Fit, File Manager sorting/actions, and Workspace shutdown lifecycle.
13. If Browser input focus still fails, attach `logs\runtime\AIEngineeringWorkspace_*.log`; rc05 retains the focus/thread/HWND diagnostics added in rc04.

## Current limits / non-goals

This remains an RC-stage engineering POC. The following are not implemented yet:

- Workspace Save / Load
- persisted pane geometry/layout mode
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
        ↓
Workspace Save / Load
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
