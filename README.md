# AI Engineering Workspace

Current version: **v0.0.5rc01**

Repository: `jkman357/AI-Engineering-Workspace`

AI Engineering Workspace is a .NET 10 / WPF Windows desktop workspace that docks real Firefox windows together with project-oriented file panes. The current POC is intentionally API-free and runs as a standard user.

## Current direction

```text
Unified Workspace Surface
├─ Browser Pane (B1 ... B8)
│  ├─ real Firefox HWND docking
│  ├─ Launch + Dock / Dock Existing / Focus / Detach
│  ├─ per-window lifecycle ownership
│  └─ Google default endpoint
├─ File Pane (F1 ... F4)
│  ├─ Windows Shell file/folder icons
│  ├─ path navigation / parent / refresh
│  └─ Windows FileDrop drag source
└─ Pane Identity
   ├─ stable internal PaneId (GUID)
   ├─ human-readable endpoint alias (B1/F1 ...)
   └─ future context/message routing target
```

## v0.0.5rc01 — Unified Dynamic Pane + Endpoint Identity

This RC removes the fixed left-file/right-browser layout and places both pane types on one unified workspace surface.

### Dynamic pane behavior

- Browser and File panes can be moved freely by dragging the `⋮⋮` handle.
- Browser and File panes can be resized with the lower-right `◢` grip.
- Dragging one pane onto another swaps their positions, regardless of pane type.
- Browser and File panes can therefore exchange locations without changing identity.
- The workspace surface expands when panes are moved or resized beyond the current bounds.
- The surface is scrollable when its working area exceeds the main window.

### Dynamic add / remove

- `+ Browser` adds Browser panes up to the POC maximum of 8.
- `+ Files` adds File panes up to the POC maximum of 4.
- Each pane has an upper-right `×` control.
- Browser display indices use the smallest currently available number from `1..8`.
- File display indices use the smallest currently available number from `1..4`.
- Example: if `B2` is deleted while `B1/B3/B4` remain, the next Browser pane becomes `B2`.
- If every File pane is removed, the next File pane starts again at `F1`.

### Endpoint identity

Every pane has two identities:

1. **Internal PaneId** — a GUID that remains bound to that pane for its lifetime.
2. **Display alias** — `B1..B8` for Browser panes and `F1..F4` for File panes.

The display alias is intended for human interaction. Future routing logic should use the internal PaneId so moving panes on screen does not change routing identity.

Use **Show IDs** to display the routing aliases. The button toggles to **Hide IDs** while aliases are visible.

```text
B1 -> Browser endpoint
B2 -> Browser endpoint
F1 -> File endpoint

Future examples:
B1 -> B3
F1 -> B2
```

No message-routing implementation is included in this RC; only the endpoint identity foundation is added.

## File panes

File panes use Windows Shell icons for folders and registered file types. Icons therefore follow the associations on the local Windows system, similar to File Explorer.

The first two default File panes open:

- `F1` -> Downloads
- `F2` -> Desktop

File panes support:

- path entry + Go
- parent folder
- refresh
- double-click folder navigation
- Name / Type / Size / Modified columns
- Shell icons for folders and file types such as EXE, TXT, ZIP, LNK, PDF, and other registered types
- Windows `FileDrop` drag source for future Browser upload workflows

## Browser panes

Browser panes default to:

```text
https://www.google.com/
```

The Browser implementation docks the **actual Firefox top-level HWND** into WPF. It does not replace Firefox with an embedded web engine.

Existing safety behavior remains:

- Firefox launch/HWND discovery is serialized to reduce cross-pane assignment races.
- The same Firefox HWND cannot be owned by two Browser panes.
- Workspace-launched Firefox windows are tracked by HWND + PID.
- Closing a Workspace-owned Browser pane gracefully closes that specific Firefox window.
- A Firefox window attached through `Dock Existing` is detached/restored instead of force-closed.
- Closing the Workspace gracefully closes Firefox windows that were launched by the Workspace.
- The application does not use `Process.Kill()` for normal Browser window lifecycle handling.

## Security and privacy

**AI Engineering Workspace does not store, collect, persist, or manage user account credentials or passwords.**

The application does not implement its own credential database, password vault, account store, or authentication provider. Login credentials, cookies, browser sessions, saved passwords, and authentication state remain under the control of Firefox and the selected Firefox user profile.

AI Engineering Workspace currently operates by hosting/docking browser windows and does not read or persist browser password-manager data.

Runtime diagnostics may contain engineering information such as URLs supplied to the launcher, local file/folder paths, process IDs, HWND values, pane aliases, and error details. Users should handle diagnostic logs according to their own privacy, security, and organizational requirements.

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

## v0.0.5rc01 POC test focus

1. Build with `build.cmd`.
2. Launch with `run.cmd`.
3. Confirm the default workspace starts with `F1`, `F2`, `B1`, `B2`, `B3`, and `B4`.
4. Drag File and Browser panes to arbitrary free positions.
5. Drag a File pane onto a Browser pane and verify the two panes swap positions while aliases stay unchanged.
6. Resize File and Browser panes with the lower-right grip.
7. Click `Show IDs` and verify `F1/F2` and `B1..B4` are visible.
8. Move panes again and confirm the displayed aliases do not change because of position.
9. Delete `B2`, add a Browser pane, and verify the new pane reuses `B2`.
10. Delete all File panes, add one File pane, and verify it is `F1`.
11. Launch/dock multiple Firefox windows and verify browser lifecycle behavior remains correct after moving/resizing panes.
12. Exit the Workspace and verify Workspace-launched Firefox windows close gracefully.

## Current limits / non-goals

This is still an RC-stage engineering POC. The following are not implemented yet:

- Workspace Save / Load
- persisted pane geometry
- automatic snap/docking layout engine
- cross-chat context/message routing
- controlled clipboard routing
- AI APIs
- browser DOM automation
- credential storage or password management

## Roadmap direction

```text
Unified Dynamic Pane
        +
Endpoint Identity
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

This software is provided **"AS IS"**, without warranty of any kind, express or implied. Users are responsible for evaluating suitability, security, privacy, regulatory/compliance impact, operational risk, and compatibility before using the software in development, production, enterprise, medical, safety-critical, or other environments.
