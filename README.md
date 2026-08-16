# AI Engineering Workspace

Current version: **v0.0.6rc15**

Repository: `jkman357/AI-Engineering-Workspace`

Copyright (c) 2026 Ray Yang. Released under the MIT License. See `LICENSE`.

AI Engineering Workspace is a .NET 10 / WPF Windows desktop engineering workspace that docks real Firefox windows together with project-oriented file panes. The active v0.0.6 line remains RC; v0.0.6 is not frozen by this package.

## Security / privacy boundary — credentials are not application data

**AI Engineering Workspace does not provide, implement, persist, collect, or manage user account credentials or passwords.** It does not implement its own password vault, credential database, account database, or authentication provider.

Browser login/session credentials remain managed by Firefox and its browser profile. Firefox cookies, sessions, saved logins, password-manager data, and account state are outside this application's storage responsibility. `.aew` Workspace files intentionally do not persist Browser URLs, history, cookies, sessions, passwords, or credentials.

Runtime diagnostics are local engineering logs and may contain paths, URLs, HWND/PID values, exceptions, or other engineering context. Best-effort sensitive-value redaction is not a guarantee; review logs before sharing. See `SECURITY.md`.

## Architecture

```text
Unified Dynamic Workspace
├─ Browser Pane (B1 ... B8)
│  ├─ real Firefox HWND docking
│  ├─ Firefox-native address bar / web-content focus
│  ├─ transactional root-HWND focus handoff
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

## v0.0.6rc15 — Browser Maximize / Restore Focus Recovery

Real-machine rc14 validation showed normal multi-Browser input working across B1-B8, including English/number input and Zhuyin composition, while the Browser Workspace maximize mode (`□`) could leave both ASCII and IME input unable to reach the docked Firefox window.

rc15 keeps the rc14 input-language/IME diagnostics unchanged and treats maximize/restore as a separate WPF-owned focus transition:

- after maximize geometry/visibility changes complete, schedule a deferred Firefox root-HWND focus recovery at `DispatcherPriority.ContextIdle`;
- after explicit restore from the Browser maximize button, restore layout first and then perform the same deferred root focus recovery;
- clear WPF keyboard focus from the maximize/restore button before handing focus to Firefox;
- refit and repaint the hosted Firefox HWND immediately before the deferred focus handoff;
- retain `FirefoxInputCoordinator` as the single transactional `AttachThreadInput` / `SetFocus` authority;
- do not synthesize IME composition messages or force input-language state.

The manual acceptance gate covers normal, maximized, and restored Browser input for both English/number and Zhuyin without requiring the `⌖` Focus recovery button.

## v0.0.6rc14 — Firefox IME / Input-Language Diagnostics

This RC continues the active **v0.0.6** line after real-machine rc13 testing established a sharper input boundary: docked Firefox can accept ordinary English/number key input, while switching to a Zhuyin/IME path can leave composition unavailable.

rc14 is intentionally an **evidence-gathering RC**, not an IME emulator. It keeps the rc13 transactional root-HWND focus handoff and adds read-only diagnostics around the Windows input-language boundary:

- records the Workspace-thread and Firefox-thread `GetKeyboardLayout` (HKL) values before/after explicit Firefox focus handoff;
- samples the docked Firefox GUI thread with `GetGUIThreadInfo` from the existing one-second health loop and logs only observable state transitions (`hwndActive`, `hwndFocus`, `hwndCaret`, foreground HWND and HKL values);
- records `WM_INPUTLANGCHANGEREQUEST`, `WM_INPUTLANGCHANGE`, `WM_IME_SETCONTEXT`, `WM_IME_STARTCOMPOSITION`, `WM_IME_COMPOSITION`, and `WM_IME_ENDCOMPOSITION` when they reach the WPF top-level HWND or the WPF-owned Browser host HWND;
- does **not** synthesize IME composition messages, call `ActivateKeyboardLayout`, or force a keyboard layout into Firefox in rc14.

The validation goal is to distinguish two materially different failures after switching English/number input to Zhuyin: (1) the Firefox thread HKL never changes, or (2) the Firefox thread HKL changes but the IME composition path still does not activate. That evidence determines the next fix without expanding the Workspace into an IME proxy.

## v0.0.6rc13 — Transactional Firefox focus handoff

rc12 proved that docked Firefox can accept keyboard input, but multi-Browser testing exposed a focus-ownership failure: one Browser pane could type while another visually focused Firefox pane could not.

rc13 removes the dock-lifetime `AttachThreadInput` ownership model. A central `FirefoxInputCoordinator` performs only a one-shot transaction when root-HWND recovery is required:

```text
WPF UI thread
   ↓
temporary AttachThreadInput
   ↓
SetFocus(Firefox root HWND)
   ↓
finally: DetachThreadInput
   ↓
Firefox owns address-bar / page / ChatGPT prompt focus
```

There is no persistent per-pane input bridge state. Normal Browser input does not enumerate, rank, or force focus into guessed Firefox compositor/content child HWNDs. `TabIntoCore` and the `Focus` toolbar action remain root-HWND recovery paths only.

The rc12 New Workspace behavior is retained: a clean Workspace gets an explicit Create New / Cancel confirmation; a dirty Workspace retains Save / Discard / Cancel semantics before reset. The default reset remains F1/F2 + B1-B4, Auto Fit, Google Browser startup, reset feedback, and no Browser session persistence.

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

Release-candidate source archives carry the version in the ZIP filename only, for example `AI-Engineering-Workspace_v0.0.6rc15.zip`. The extracted project root remains exactly `AI-Engineering-Workspace\` so repository paths, scripts, comparisons, and command-line workflows do not change between RC packages.

## Workspace project (`.aew`)

The local JSON Workspace format persists layout state only: pane type, stable PaneId, F#/B# display index, geometry, layout mode, Show IDs state, File-pane folder paths, and main-window geometry/state. Browser session/navigation/authentication state is intentionally excluded.

## Native Shell integration

File-pane right-click menus come from Windows Shell. Installed Shell extensions such as TortoiseGit, 7-Zip, compare tools, security products, and cloud-storage clients may participate. Those extensions are an external trust boundary and execute according to Windows and the installed product, not AI Engineering Workspace.

## Disclaimer

This project is RC-stage engineering software. It is provided **AS IS**, without warranty. Production, enterprise, medical, safety-critical, regulated, or other high-assurance adoption requires independent validation, risk analysis, security/privacy review, regulatory/compliance assessment, and operational qualification. This guidance does not add restrictions to or modify the permissions granted by the MIT License.
