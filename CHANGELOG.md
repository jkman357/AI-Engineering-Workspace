# Changelog

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
