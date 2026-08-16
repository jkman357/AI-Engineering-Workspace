# AI Engineering Workspace

Current version: **v0.0.6rc22**

Repository: `jkman357/AI-Engineering-Workspace`

Copyright (c) 2026 Ray Yang. Released under the MIT License. See `LICENSE`.

AI Engineering Workspace is a .NET 10 / WPF Windows desktop engineering workspace that combines real Firefox Browser panes with project-oriented File Manager panes. The active v0.0.6 line remains RC; v0.0.6 is not frozen by this package.

For version history, see `CHANGELOG.md`. For per-RC engineering rationale and validation gates, see `docs/releases/`.

## Security / privacy boundary — credentials are not application data

**AI Engineering Workspace does not provide, implement, persist, collect, or manage user account credentials or passwords.** It does not implement its own password vault, credential database, account database, or authentication provider.

Browser login/session credentials remain managed by Firefox and its browser profile. Firefox cookies, sessions, saved logins, password-manager data, and account state are outside this application's storage responsibility. `.aew` Workspace files intentionally do not persist Browser URLs, history, cookies, sessions, passwords, or credentials.

Runtime diagnostics are local engineering logs and may contain paths, URLs, HWND/PID values, exceptions, or other engineering context. Best-effort sensitive-value redaction is not a guarantee; review logs before sharing. See `SECURITY.md`.

## Architecture

```text
Unified Dynamic Workspace
├─ Browser Pane (B1 ... B8)
│  ├─ real Firefox HWND docking
│  ├─ Firefox-native address bar / web-content / IME ownership
│  ├─ no Firefox child-HWND focus guessing
│  ├─ read-only input-language / IME diagnostics
│  ├─ explicit transactional root-HWND recovery only where required
│  ├─ Workspace maximize / restore
│  ├─ Launch + Dock / Dock Existing / Focus / Detach
│  └─ per-window lifecycle ownership
├─ File Pane (F1 ... F4)
│  ├─ Windows Shell file/folder icons
│  ├─ native Windows Shell IContextMenu
│  ├─ 7-Zip / compare / TortoiseGit Shell-extension participation
│  ├─ asynchronous Git working-tree badges
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

## Browser input boundary

The Workspace must not act as an IME proxy. Firefox remains responsible for address-bar, page-content, text-edit, input-language, TSF, and IME state.

The Browser input policy is:

- do not enumerate or guess Firefox compositor/content child HWNDs for normal input;
- do not synthesize `WM_IME_*` composition messages;
- do not call `ActivateKeyboardLayout` for Firefox;
- do not post `WM_INPUTLANGCHANGEREQUEST` into Firefox as corrective synchronization;
- keep HKL and GUI-thread observations diagnostic-only;
- do not maintain a persistent `AttachThreadInput` bridge;
- when explicit root-HWND recovery is required, use a one-shot `AttachThreadInput -> SetFocus(root) -> immediate detach` transaction and let Firefox resume internal focus ownership.

v0.0.6rc22 intentionally returns to the rc15-era input architecture after the rc16-rc21 investigation line demonstrated that active input-language/focus intervention could confound Firefox IME state. Detailed history belongs in `CHANGELOG.md` and `docs/releases/v0.0.6rc22.md`, not in this README.

## Build / test / run

Requirements:

- Windows 10/11
- Visual Studio 2026 or a compatible .NET 10 SDK toolchain
- .NET 10 SDK
- Firefox for Browser docking tests

From the fixed project root:

```bat
cd AI-Engineering-Workspace
build.cmd
test.cmd
run.cmd
```

Logs are written under `logs\build`, `logs\test`, and `logs\runtime`.

## Source package convention

Release-candidate source archives carry the version in the ZIP filename only, for example `AI-Engineering-Workspace_v0.0.6rc22.zip`. The extracted project root remains exactly `AI-Engineering-Workspace\` so repository paths, scripts, comparisons, and command-line workflows do not change between RC packages.

## Workspace project (`.aew`)

The local JSON Workspace format persists layout state only: pane type, stable PaneId, F#/B# display index, geometry, layout mode, Show IDs state, File-pane folder paths, and main-window geometry/state. Browser session/navigation/authentication state is intentionally excluded.

## Native Shell integration

File-pane right-click menus come from Windows Shell. Installed Shell extensions such as TortoiseGit, 7-Zip, compare tools, security products, and cloud-storage clients may participate. Those extensions are an external trust boundary and execute according to Windows and the installed product, not AI Engineering Workspace.

## Disclaimer

This project is RC-stage engineering software. It is provided **AS IS**, without warranty. Production, enterprise, medical, safety-critical, regulated, or other high-assurance adoption requires independent validation, risk analysis, security/privacy review, regulatory/compliance assessment, and operational qualification. This guidance does not add restrictions to or modify the permissions granted by the MIT License.
