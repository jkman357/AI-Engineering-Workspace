# Changelog

## v0.0.6rc22

- continue the active v0.0.6 RC line without freezing or advancing the minor version;
- return Browser docking/input behavior to the rc15-era baseline after the rc16-rc21 investigation line;
- retain `SetParent` Firefox docking and the rc13/rc15 centralized one-shot root-HWND recovery transaction rather than the rc16 native-click coordinator, rc17 persistent bridge, or rc18-rc21 pass-through/pseudo-dock/launch-only experiments;
- remove the rc16+ corrective input-language design from the active code path: do not post `WM_INPUTLANGCHANGEREQUEST` into Firefox, do not call `ActivateKeyboardLayout`, and do not synthesize IME composition;
- keep Workspace/Firefox HKL, foreground, `GetGUIThreadInfo`, and IME/input-language message instrumentation read-only;
- do not add `WM_PARENTNOTIFY` / `WM_MOUSEACTIVATE` driven automatic root-focus handoff on every native Firefox click;
- retain no persistent `AttachThreadInput` bridge and keep temporary attach / root `SetFocus` / immediate detach limited to explicit root-HWND recovery paths from the rc15 baseline;
- retain rc15 maximize/restore deferred recovery, New Workspace confirmation, `.aew` persistence, Auto Fit/Free Layout, endpoint badges, File Manager, native Shell/TortoiseGit/7-Zip integration, asynchronous Git decoration, security/privacy wording, standard-user design, API-free architecture, and MIT License;
- preserve rc16-rc21 release notes as investigation history even though their experimental Browser input architectures are not carried into rc22;
- split README and CHANGELOG responsibilities: README contains current product/architecture/use information only, CHANGELOG contains version history, and `docs/releases/` contains per-RC engineering rationale and validation gates;
- require a fresh Firefox process for the first rc22 IME validation so any process-level state left by earlier experimental RCs does not invalidate the result;
- stamp metadata as `v0.0.6rc22` / `0.0.6-rc22` / FileVersion `0.0.6.22`.

## v0.0.6rc21

- continue the active v0.0.6 RC line without freezing the release;
- use rc20 real-machine evidence that Zhuyin still fails after removing Firefox parent, owner, style, input-queue, focus, and HKL mutations;
- turn the Browser experiment into a strict Firefox launch-only control: launch a new Firefox window, discover its native top-level HWND, then stop modifying that window;
- remove `SetWindowPos`, `SetWindowPlacement`, Workspace-driven `ShowWindow`, geometry synchronization, visibility synchronization, and detach-time placement restoration from the Browser path;
- suppress explicit Workspace focus recovery and Workspace-driven keyboard navigation so rc21 uses no `SetForegroundWindow`, root `SetFocus`, `AttachThreadInput`, or `SendInput` against Firefox;
- keep the tracked HWND only for health/foreground/HKL diagnostics, pane identity, duplicate-claim protection, and Workspace-owned Firefox shutdown cleanup;
- leave Firefox at its own native Windows position, size, title bar, Z-order, focus, TSF, and IME lifecycle;
- document B1-only `abc123` → Zhuyin → `你好` as the control gate before any multi-Browser UX work;
- stamp metadata as `v0.0.6rc21` / `0.0.6-rc21` / FileVersion `0.0.6.21`.

## v0.0.6rc20

- continue the active v0.0.6 RC line without freezing the release;
- use rc19 real-machine evidence that removing `SetParent` alone did not restore Zhuyin/Chinese IME composition;
- keep Firefox as a native top-level window and remove rc19 owner reassignment through `GWL_HWNDPARENT`;
- remove rc19 `GWL_STYLE` / `GWL_EXSTYLE` mutation and intentionally retain the native Firefox title bar/frame as an A/B control;
- restrict pseudo-dock transformation to screen-rectangle synchronization through `SetWindowPos(... SWP_NOACTIVATE ...)` plus visibility show/hide;
- retain no `SetParent`, no `AttachThreadInput`, no root `SetFocus`, no HKL synchronization, no synthetic IME composition, and no Firefox child-HWND focus guessing;
- retain explicit `SetForegroundWindow` only for the Focus/recovery action;
- add diagnostics proving `OwnerMutation=False` and `StyleMutation=False`;
- document a B1-only English/number then Zhuyin `你好` control gate before any B2-B8 expansion;
- stamp metadata as `v0.0.6rc20` / `0.0.6-rc20` / FileVersion `0.0.6.20`.

## v0.0.6rc19

- replace `SetParent` Firefox embedding with a native top-level pseudo-dock prototype after rc18 real-machine testing showed English/number input PASS but Zhuyin/IME composition FAIL;
- keep `BrowserDockHost` as a WPF/native geometry anchor only and never make Firefox a `WS_CHILD`;
- strip visible Firefox top-level caption/frame chrome while pseudo-docked, assign the Workspace HWND only as window owner, and restore original owner/style/placement on detach;
- mirror each Browser pane to screen coordinates with `GetWindowRect` + `SetWindowPos(... SWP_NOACTIVATE ...)`;
- synchronize pseudo-dock geometry across pane/layout resize, Workspace move, scrolling, viewport changes, activation, maximize/restore, and Window restore;
- hide pseudo-docked Firefox windows while panes or the Workspace are hidden/minimized and resynchronize them when visible again;
- remove `AttachThreadInput` and root `SetFocus` from the rc19 Browser architecture; explicit Focus/TabIntoCore recovery now uses native top-level `SetForegroundWindow`;
- keep HKL, `GetGUIThreadInfo`, input-language, and IME diagnostics observation-only; continue to avoid HKL forcing, IME synthesis, and Firefox internal child-HWND focus guessing;
- document pseudo-dock prototype limitations for scrolling/clipping, overlapping panes, multi-monitor DPI, minimize/restore, and external Firefox-window isolation;
- add regression checks and a staged real-machine gate beginning with B1 English/number + Zhuyin before scaling to B1-B4/B1-B8;
- stamp application/build/run/test metadata as `v0.0.6rc19` / `0.0.6-rc19` / FileVersion `0.0.6.19`;
- continue the active v0.0.6 RC line without freezing the release.

## v0.0.6rc18

- reverse rc17 persistent input-queue bridging after real-machine testing showed correct B7 root-HWND focus while typed text could still route to B8;
- make normal docked Firefox interaction native pass-through: Browser mouse activation is observed for pane ownership/z-order but does not call `SetFocus` or `AttachThreadInput`;
- remove central persistent `AttachThreadInput` bridge state, reference counts, and automatic root-focus handoff from normal Browser clicks and dock lifecycle;
- retain a temporary attach / root `SetFocus` / immediate detach transaction only for explicit recovery paths such as `⌖ Focus`, `TabIntoCore`, and Workspace-driven keyboard navigation;
- make top Workspace toolbar buttons and Browser pane chrome buttons non-focusable / non-tab-stop so Show IDs, Auto Fit, add, maximize, detach, and related mouse commands do not intentionally take keyboard focus from Firefox;
- replace maximize/restore focus recovery with deferred native repaint only;
- change HKL mismatch handling back to diagnostic-only observation and stop posting `WM_INPUTLANGCHANGEREQUEST` into Firefox;
- continue to avoid Firefox compositor/content child-HWND enumeration, synthetic IME composition, and `ActivateKeyboardLayout`;
- add regression checks and a real-machine B1-B8 gate for English/number + Zhuyin routing, Show IDs, add/close/reopen, and B7→B8 isolation with no Focus-button dependency;
- stamp application/build/run/test metadata as `v0.0.6rc18` / `0.0.6-rc18` / FileVersion `0.0.6.18`;
- continue the active v0.0.6 RC line without freezing the release.

## v0.0.6rc17

- replace rc16 one-shot attach/focus/detach transactions with one central persistent `AttachThreadInput` bridge per unique Workspace-thread / Firefox-thread pair;
- register each dock centrally and reference-count all Browser roots sharing the same Firefox input thread;
- keep the bridge connected while any dock on that thread remains, and detach it only when the final registered dock is removed;
- keep persistent bridge ownership out of `BrowserDockHost` so individual pane lifecycle cannot detach a bridge still required by sibling Browser panes;
- switch B1/B2/B3/B4 input ownership with root-HWND `SetFocus` only while the shared input queues remain connected;
- use `WM_PARENTNOTIFY` mouse-down as the single native focus handoff and suppress the duplicate `WM_MOUSEACTIVATE` focus transaction;
- retain active-Browser layout recovery, Show IDs handling, request-based HKL synchronization, Firefox child-HWND focus-guessing removal, and non-proxy IME design;
- add regression coverage and a real-machine gate specifically for text routing across B3 → B1 → B2 → B4 → B3 without input remaining stuck in the previous Browser;
- stamp application/build/run/test metadata as `v0.0.6rc17` / `0.0.6-rc17` / FileVersion `0.0.6.17`;
- continue the active v0.0.6 RC line without freezing the release.

## v0.0.6rc16

- centralize active docked Firefox root-HWND ownership in `FirefoxInputCoordinator`;
- recover Firefox keyboard focus from native Browser mouse activation (`WM_PARENTNOTIFY` / `WM_MOUSEACTIVATE`) instead of relying on WPF `PreviewMouseDown` over hosted Win32 content;
- keep cross-thread focus recovery transactional with temporary `AttachThreadInput` and immediate detach;
- recover only the active Browser after Show IDs, layout-mode, Add Browser, viewport resize, and maximize/restore transitions;
- clear active-Browser ownership when a File pane is activated to avoid focus theft from File Manager;
- synchronize a stale Firefox-thread HKL by posting `WM_INPUTLANGCHANGEREQUEST` to the active Firefox root after WPF input-language changes;
- continue to avoid Firefox child-HWND focus guessing, `ActivateKeyboardLayout`, and synthetic IME composition;
- add regression coverage and a B1-B8 real-machine gate for repeated typing, Show IDs, close/reopen, English/number + Zhuyin, and maximize/restore;
- stamp application/build/run/test metadata as `v0.0.6rc16` / `0.0.6-rc16` / FileVersion `0.0.6.16`;
- continue the active v0.0.6 RC line without freezing the release.

## v0.0.6rc15

- restore Firefox root-HWND keyboard focus after Browser Workspace maximize/restore transitions;
- defer focus recovery until WPF visibility/geometry changes and native Firefox repaint have completed;
- clear WPF button focus before the transactional Firefox root focus handoff;
- keep ordinary multi-Browser English/number and Zhuyin behavior from rc14 unchanged;
- retain rc14 read-only HKL / GUI-thread / IME diagnostics without synthesizing IME messages or forcing keyboard-layout changes;
- add regression coverage and a real-machine gate for normal, maximized, and restored Browser input without using the Focus button;
- stamp application/build/run/test metadata as `v0.0.6rc15` / `0.0.6-rc15` / FileVersion `0.0.6.15`;
- continue the active v0.0.6 RC line without freezing the release.

## v0.0.6rc14

- continue the active v0.0.6 RC line without freezing the release;
- retain rc13 transactional Firefox root-HWND focus handoff for ordinary keyboard input;
- add read-only Workspace-thread / Firefox-thread keyboard-layout (HKL) diagnostics;
- add `GetGUIThreadInfo` evidence for Firefox active/focus/caret HWND state;
- sample input state from the existing Browser health loop and log only observable state transitions;
- observe input-language and IME boundary messages on the WPF MainWindow and WPF-owned Browser host HWNDs;
- explicitly avoid synthesizing IME composition messages or forcing Firefox keyboard-layout changes before root-cause evidence is captured;
- add an rc14 manual gate for English/number -> Zhuyin switching and corresponding runtime-log evidence;
- stamp application/build/run/test metadata as `v0.0.6rc14` / `0.0.6-rc14` / FileVersion `0.0.6.14`;
- retain rc12 New Workspace confirmation behavior and the remaining rc11/rc13 Workspace, Shell/Git, security/privacy, standard-user, API-free, and MIT-license behavior.

## v0.0.6rc13

- replace rc12 dock-lifetime `AttachThreadInput` bridges with centralized one-shot Firefox root-HWND focus handoff transactions;
- add `FirefoxInputCoordinator` as the only owner of cross-thread attach/focus/detach operations;
- detach the temporary input queue bridge in `finally` immediately after the root focus attempt;
- remove persistent bridge state/lifecycle ownership from `BrowserDockHost`;
- keep normal Browser input free of compositor/content child-HWND enumeration or focus guessing;
- retain `TabIntoCore` and Focus toolbar behavior as root-HWND recovery only;
- retain Workspace/Firefox thread, PID/HWND, foreground, previous/current focus, and attach/detach diagnostics;
- document a B1/B2/B3/B4 plus B2→B3→B2 multi-pane manual keyboard PASS gate without using Focus first;
- retain rc12 explicit New Workspace confirmation and dirty Save / Discard / Cancel behavior;
- retain rc11 `.aew` persistence, endpoint badges, compact version display, native Shell integration, asynchronous Git decoration, Firefox launch ownership cleanup, diagnostics/security boundaries, standard-user/API-free design, and MIT License;
- stamp application/build/run/test metadata as `v0.0.6rc13` / `0.0.6-rc13` / FileVersion `0.0.6.13`.

## v0.0.6rc12

- kept a persistent input-queue bridge between WPF and the docked Firefox root-window thread for the lifetime of each dock;
- removed normal child-HWND focus guessing;
- added explicit New Workspace confirmation for clean workspaces and retained Save / Discard / Cancel for dirty workspaces;
- stamped metadata as `v0.0.6rc12` / FileVersion `0.0.6.12`.

## v0.0.6rc11

- moved 64×64 Show IDs badges into WPF-owned pane header chrome;
- suppressed source-revision suffixes in visible version strings;
- made New Workspace a deterministic repeated reset with visible feedback.

## v0.0.6rc10

- introduced `.aew` Workspace project New/Open/Save/Save As;
- persisted pane identity, geometry, layout, Show IDs and File paths while excluding Browser session/authentication state.

## v0.0.6rc09

- hardened Auto Fit, asynchronous Git decoration, Firefox pending-launch cleanup, Git rename parsing, and regression coverage.

Earlier v0.0.6 RC history remains represented by the repository release-note lineage under `docs/releases/`.
