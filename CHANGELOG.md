# Changelog

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
