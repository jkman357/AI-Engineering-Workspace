# Changelog

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
