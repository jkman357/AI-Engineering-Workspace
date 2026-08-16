# Changelog

## v0.0.6rc10

- add New / Open / Save / Save As Workspace-project commands with a human-readable `.aew` JSON format;
- persist pane type, stable `PaneId`, F#/B# display index, X/Y geometry, width/height, layout mode, Show IDs state, and File-pane folder paths;
- restore missing saved File paths to Desktop without failing the entire Workspace load;
- intentionally do not persist Browser current URLs, browser history, login/session state, or credentials; restored Browser panes continue to launch Firefox at Google;
- add unsaved-change (`*`) tracking and Save / Don't Save / Cancel prompts for New, Open, and application close;
- keep compact boxed F#/B# aliases permanently visible and remove duplicate visible `Files N` / `Browser N` title text;
- add a 64×64 color-coded endpoint overlay when Show IDs is enabled while retaining `PaneId` as the stable internal identity;
- add Workspace-project round-trip and endpoint-badge regression checks;
- stamp executable/build/test metadata as `v0.0.6rc10` / `0.0.6.10` from `Directory.Build.props`;
- retain rc09 review hardening, native Shell integration, Git status, Firefox HWND lifecycle, diagnostics/security boundaries, and the MIT License unchanged.

## v0.0.6rc09

- fix Auto Fit pane overlap by introducing a minimum-size-aware layout planner that enlarges the Canvas and scrolls when the viewport cannot physically contain all panes;
- move Git probing off the WPF dispatcher so File panes render immediately, then apply cancellable/cached Git decorations asynchronously;
- make Git subprocess waiting cancellation-aware and prevent stale navigation generations from updating the current File pane;
- add pending Firefox launch ownership/cleanup inside `FirefoxWindowService`, including synchronous shutdown cleanup for the Process.Start-to-HWND-discovery gap;
- fix porcelain `R`/`C` parsing so status remains on the current/destination path;
- collapse the Git badge overlay when no Git glyph exists;
- centralize executable version metadata in `Directory.Build.props` and derive command-line version labels from it;
- add a dependency-free regression test project plus `test.cmd` for layout, Git parser, badge, and version invariants;
- continue the active v0.0.6 RC line without freezing the release.

## v0.0.6rc08

- make Auto Fit rebuild pane placement deterministically from endpoint identity and reset Workspace scroll offsets to the top-left;
- add a post-render Browser fit/redraw pass after Auto Fit to prevent apparent blank-origin layout artifacts;
- remove the duplicate Workspace Browser URL TextBox and return normal browsing/navigation to Firefox itself;
- add final Win32 redraw handling after Browser resize/reflow to reduce stale HWND resize pixels and ghosting;
- add color-coded B1-B8 / F1-F4 endpoint badges while preserving PaneId/alias as the actual routing identity;
- retain native Windows Shell context menus, Git status badges, Free Layout resize, security hardening, and MIT licensing from rc06;
- continue the active v0.0.6 RC line without freezing the release.

## v0.0.6rc06

- made eight-direction Free Layout resize hit areas explicitly hit-testable and wider;
- raised pane resize chrome above File/Browser content for reliable border dragging;
- replaced the custom File-pane right-click menu with native Windows Shell `IContextMenu` hosting;
- forward dynamic Shell menu messages for registered extensions such as 7-Zip, compare tools, and TortoiseGit;
- added lightweight Git working-tree badges using local `git.exe` status information;
- retained keyboard file operations as fallback actions;
- documented third-party Shell extensions as an external trust boundary;
- continued the active v0.0.6 RC line without freezing the release.

## v0.0.6rc05

- fix `GetCurrentThreadId` P/Invoke to use `kernel32.dll` instead of `user32.dll`
- replace the lower-right-only pane resize grip with true eight-direction edge/corner resize chrome
- add left, right, top, bottom, top-left, top-right, bottom-left, and bottom-right resize directions
- add standard horizontal, vertical, and diagonal resize cursors
- inset pane content to keep a WPF-owned resize hit band available around Browser/File panes, including around a foreign Firefox HWND
- make left/top resize update pane position while preserving the opposite edge
- enforce minimum pane dimensions and prevent negative top/left workspace coordinates
- keep a docked Firefox HWND fitted to its Browser pane during resize
- remove the old visible `◢` lower-right resize grip
- retain rc04 Browser focus diagnostics, input bridge, toolbar icons/tooltips, adaptive layout, File Manager UX, security wording, and MIT license

## v0.0.6rc04

- release WPF keyboard focus from the Workspace URL TextBox before transferring input to Firefox
- add a best-effort browser-content focus bridge for the foreign docked Firefox HWND
- enumerate visible Firefox child HWNDs and prefer Mozilla compositor/content surfaces for keyboard focus when available
- return focus toward browser content after Workspace URL navigation so Ctrl+L does not intentionally leave input trapped in the address bar
- bridge WPF Tab/GotKeyboardFocus entry into BrowserDockHost toward the docked Firefox content surface
- expand focus diagnostics with selected target HWND, Firefox PID/thread, foreground HWND, previous/current focus HWND, and AttachThreadInput evidence
- keep the Focus action as a manual recovery/diagnostic command rather than a normal per-input requirement
- replace Browser Launch + Dock / Dock Existing / Focus / Detach text controls with compact icon buttons and tooltips
- retain v0.0.6rc03 adaptive layout, File Manager UX, security hardening, logging policy, MIT license, and browser lifecycle behavior

## v0.0.6rc03

- add Auto Fit / Free Layout modes; Auto Fit reflows remaining panes after add/remove and on viewport changes
- add Browser pane maximize/restore inside the Workspace without using Firefox F11 monitor full-screen
- keep the docked Firefox HWND fitted to the Browser pane client area during resize/maximize/restore
- add clickable File Manager column sorting for Name, Type, Size, and Modified with ascending/descending indicators
- retain Explorer-like file context actions and keyboard shortcuts
- harden credential wording as design-intent language instead of an absolute guarantee
- document Firefox profile/session data as an external trust boundary
- add runtime diagnostic policy: documented location, default 14-day/50-file retention, 10 MB rotation, environment-variable disable/configuration, and best-effort sensitive-value redaction
- keep `asInvoker` wording scoped to standard-user design rather than an absolute non-elevation guarantee

## v0.0.6rc02

- retain v0.0.6rc01 application behavior with no intended functional change
- clarify that README disclaimer text is practical risk guidance rather than an additional use restriction
- explicitly state that README guidance does not restrict or modify permissions granted by the MIT License
- keep the standard MIT `LICENSE` text unchanged
- clarify that production, enterprise, medical, safety-critical, regulated, and other high-assurance adoption requires independent risk, validation, security, privacy, reliability, regulatory, compliance, and operational evaluation
- reinforce that the application does not store, collect, persist, or manage user account credentials/passwords and does not intentionally access or maintain browser password-manager data

## v0.0.6rc01

- start the Workspace maximized and auto-fit the default pane layout to the measured viewport
- add Enter-to-navigate behavior for each docked Browser URL box
- keep Firefox auto-fitted inside its pane without forcing F11 full-screen mode
- replace top toolbar text actions with compact icons plus tooltips
- add File pane Explorer-style context menu operations: Open, Copy, Cut/Move, Paste, Rename, Delete to Recycle Bin, New Folder, Refresh
- add File pane Ctrl+C / Ctrl+X / Ctrl+V / F2 / Delete shortcuts
- open files through Windows shell association
- retain dynamic pane identity/index reuse, Shell icons, drag/drop, and Firefox HWND lifecycle controls

## v0.0.5rc01

- unify File and Browser panes on one free-position workspace surface
- add pane move handles and resize grips
- allow File and Browser panes to exchange positions by drag/swap
- replace monotonically increasing display numbers with smallest-free-index reuse
- define Browser aliases `B1..B8` and File aliases `F1..F4`
- add stable per-pane internal GUID `PaneId`
- add `Show IDs` / `Hide IDs` endpoint-identity toggle
- retain Windows Shell file icons and Firefox HWND lifecycle protections
- add repository-facing copyright, MIT license, security/privacy notes, and AS-IS disclaimer
- explicitly document that account credentials/passwords are not stored or managed by the application

## v0.0.4rc01

- add dynamic Browser/File pane creation and removal
- move File panes to the left and Browser panes to the right
- add Windows Shell icons to File panes
- set Google as the default Browser endpoint

## v0.0.3rc01

- add two File panes and per-Browser close buttons

## v0.0.2rc02

- close Workspace-owned Firefox windows gracefully on Workspace shutdown

## v0.0.2rc01

- add 2x2 multi-Browser workspace POC

## v0.0.1rc03

- harden single-Firefox docking lifecycle
