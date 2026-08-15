# AI Engineering Workspace

Current version: **v0.0.6rc03**

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
- panes can be resized with the lower-right `◢` grip;
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

Default runtime-log policy in `v0.0.6rc03`:

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

## v0.0.6rc03 test focus

1. Run `build.cmd`.
2. Run `run.cmd` and verify Auto Fit occupies the available Workspace.
3. Delete one or more panes and verify remaining panes automatically reflow/fill the area.
4. Add File/Browser panes and verify Auto Fit recalculates the layout.
5. Drag or resize a pane and verify layout changes to Free Layout rather than snapping back automatically.
6. Toggle the layout icon back to Auto Fit and verify panes reflow.
7. Dock Firefox, press the Browser `□`, and verify that Browser pane maximizes inside the Workspace; press `❐` to restore.
8. Resize/restore and verify Firefox continues filling the Browser client area.
9. Click Name / Type / Size / Modified headers repeatedly and verify ascending/descending sorting.
10. Re-test right-click Open/Copy/Cut/Paste/Rename/Delete/New Folder/Refresh.
11. Enter `www.yahoo.com.tw` in a docked Browser pane and verify Enter navigates that pane only.
12. Verify `#` still exposes B#/F# endpoint aliases and IDs remain stable after movement.
13. Verify runtime logs rotate/configure according to the documented environment variables when exercised.
14. Exit the Workspace and verify Workspace-launched Firefox windows close gracefully.

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
