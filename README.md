# AI Engineering Workspace

Current version: **v0.0.6rc02**

Repository: `jkman357/AI-Engineering-Workspace`

AI Engineering Workspace is a .NET 10 / WPF Windows desktop workspace that docks real Firefox windows together with project-oriented file panes. The current POC is intentionally API-free and runs as a standard user.

## Current direction

```text
Unified Dynamic Workspace
├─ Browser Pane (B1 ... B8)
│  ├─ real Firefox HWND docking
│  ├─ URL + Enter navigation for the exact docked pane
│  ├─ pane-content auto-fit
│  ├─ Launch + Dock / Dock Existing / Focus / Detach
│  ├─ per-window lifecycle ownership
│  └─ Google default endpoint
├─ File Pane (F1 ... F4)
│  ├─ Windows Shell file/folder icons
│  ├─ path navigation / parent / refresh
│  ├─ Explorer-style right-click file actions
│  └─ Windows FileDrop drag source
└─ Pane Identity
   ├─ stable internal PaneId (GUID)
   ├─ human-readable endpoint alias (B1/F1 ...)
   └─ future context/message routing target
```

## v0.0.6rc02 — Workspace Fit + Browser Navigation + File Actions

This RC retains the v0.0.6rc01 application behavior and clarifies repository-facing license, disclaimer, and credential-handling wording. No functional behavior change is intended in rc02.

The underlying workspace features remain focused on practical usability found during v0.0.5rc01 testing.

### Startup workspace fit

- The main window starts maximized.
- The default `F1/F2 + B1..B4` arrangement is calculated from the actual Workspace viewport at startup.
- The six default panes are therefore sized to occupy the available working surface instead of using one fixed pixel layout that may leave large unused areas on different screens.
- The Workspace remains scrollable after the user moves, resizes, or adds panes beyond the current viewport.

### Browser pane behavior

Browser panes default to:

```text
https://www.google.com/
```

The Browser implementation docks the **actual Firefox top-level HWND** into WPF. It does not replace Firefox with an embedded web engine.

A Browser URL box now supports **Enter**:

- If Firefox is already docked in that pane, the Workspace focuses that exact HWND and requests navigation using `Ctrl+L`, the normalized URL, and Enter.
- If no Firefox is docked yet, pressing Enter launches and docks Firefox using the typed URL.
- If a scheme is omitted, common URL text such as `www.yahoo.com.tw` is normalized to `https://www.yahoo.com.tw/`.

Browser content automatically fills the remaining Browser pane content area after the Workspace title/controls. The application intentionally does **not** force Firefox into F11 full-screen mode, because the Workspace must retain pane identity, close controls, status, and future routing UI.

Existing Firefox safety behavior remains:

- Firefox launch/HWND discovery is serialized to reduce cross-pane assignment races.
- The same Firefox HWND cannot be owned by two Browser panes.
- Workspace-launched Firefox windows are tracked by HWND + PID.
- Closing a Workspace-owned Browser pane gracefully closes that specific Firefox window.
- A Firefox window attached through `Dock Existing` is detached/restored instead of force-closed.
- Closing the Workspace gracefully closes Firefox windows that were launched by the Workspace.
- The application does not use `Process.Kill()` for normal Browser window lifecycle handling.

## Dynamic pane behavior

- Browser and File panes can be moved freely by dragging the `⋮⋮` handle.
- Browser and File panes can be resized with the lower-right `◢` grip.
- Dragging one pane onto another swaps their positions, regardless of pane type.
- Browser and File panes can therefore exchange locations without changing identity.
- The workspace surface expands when panes are moved or resized beyond the current bounds.
- The surface is scrollable when its working area exceeds the main window.

### Dynamic add / remove

The top toolbar uses compact icon controls with tooltips:

- `📁+` — add File pane
- `🌐+` — add Browser pane
- `#` — show/hide routing endpoint IDs
- `↗` — detach all docked Firefox windows

Limits for the current POC:

- Browser panes: `B1..B8`
- File panes: `F1..F4`

Display indices use the smallest free number. Example: if `B2` is deleted while `B1/B3/B4` remain, the next Browser pane becomes `B2`. If every File pane is removed, the next File pane starts again at `F1`.

## Endpoint identity

Every pane has two identities:

1. **Internal PaneId** — a GUID that remains bound to that pane for its lifetime.
2. **Display alias** — `B1..B8` for Browser panes and `F1..F4` for File panes.

The display alias is intended for human interaction. Future routing logic should use the internal PaneId so moving panes on screen does not change routing identity.

Use the `#` toolbar control to show/hide routing aliases.

```text
B1 -> Browser endpoint
B2 -> Browser endpoint
F1 -> File endpoint

Future examples:
B1 -> B3
F1 -> B2
```

No message-routing implementation is included in this RC; only the endpoint identity foundation is present.

## File panes

File panes use Windows Shell icons for folders and registered file types. Icons therefore follow the associations on the local Windows system, similar to File Explorer.

The first two default File panes open:

- `F1` -> Downloads
- `F2` -> Desktop

File panes support:

- path entry + Go / Enter
- parent folder
- refresh
- double-click folder navigation
- double-click file open through the registered Windows shell application
- Name / Type / Size / Modified columns
- Windows Shell icons for folders and registered file types
- Windows `FileDrop` drag source for Browser upload workflows

### File right-click actions

Right-click in a File pane provides:

- Open
- Copy
- Cut / Move
- Paste
- Rename
- Delete to Recycle Bin
- New Folder
- Refresh

Keyboard shortcuts are also supported for the focused File pane:

```text
Ctrl+C  Copy
Ctrl+X  Cut / Move
Ctrl+V  Paste
F2      Rename
Delete  Delete to Recycle Bin
```

Paste operations do not silently overwrite an existing destination item. Name collisions are skipped and logged for this RC.

## Security and privacy

**AI Engineering Workspace does not store, collect, persist, or manage user account credentials or passwords.**

The application does not implement its own credential database, password vault, account store, or authentication provider. Login credentials, cookies, browser sessions, saved passwords, and authentication state remain under the control of Firefox and the selected Firefox user profile.

AI Engineering Workspace currently operates by hosting/docking browser windows. It does not implement credential capture or persistence and does not intentionally access or maintain browser password-manager data.

Runtime diagnostics may contain engineering information such as URLs supplied to Browser panes, local file/folder paths, process IDs, HWND values, pane aliases, and error details. Users should handle diagnostic logs according to their own privacy, security, and organizational requirements.

## Standard-user / IT-policy principle

The executable manifest uses `asInvoker`; Administrator privileges are not required by design.

Build/run scripts do not use PowerShell execution-policy bypasses or attempt to circumvent endpoint-management or IT security policy. If an environment blocks an operation, the application should fail visibly and leave diagnostic evidence rather than silently bypass the policy.

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

The build log is written to:

```text
logs\build\build_*.log
```

The project must remain buildable from command line without opening Visual Studio.

## Run

```bat
run.cmd
```

Runtime launcher and application logs are written under:

```text
logs\runtime\
```

## v0.0.6rc02 POC test focus

1. Build with `build.cmd`.
2. Launch with `run.cmd` and verify the main window starts maximized.
3. Verify default `F1/F2 + B1..B4` panes occupy the available Workspace surface without the previous large unused area.
4. Launch/dock a Browser pane, type `www.yahoo.com.tw`, press Enter, and verify that exact Browser pane navigates.
5. Resize the Browser pane and verify the Firefox HWND continues filling its content area.
6. Hover the four toolbar icons and verify each tooltip explains its action.
7. Right-click a file/folder and test Open, Copy, Cut/Move, Paste, Rename, Delete to Recycle Bin, New Folder, and Refresh.
8. Verify Ctrl+C / Ctrl+X / Ctrl+V / F2 / Delete operate on the focused File pane.
9. Move and resize File/Browser panes and verify `B#` / `F#` identity remains stable.
10. Close/re-add panes and verify smallest-free display index reuse remains correct.
11. Exit the Workspace and verify Workspace-launched Firefox windows close gracefully.

## Current limits / non-goals

This is still an RC-stage engineering POC. The following are not implemented yet:

- Workspace Save / Load
- persisted pane geometry
- automatic snap/docking layout engine
- cross-chat context/message routing
- controlled clipboard routing between Browser endpoints
- AI APIs
- browser DOM automation
- credential storage or password management

Browser URL navigation in this RC uses controlled keyboard input to the exact docked Firefox HWND. It is a POC-level API-free navigation mechanism and should be tested for focus behavior across different Windows/Firefox configurations.

## Roadmap direction

```text
Unified Dynamic Pane
        +
Endpoint Identity
        +
Practical File/Browser UX
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
