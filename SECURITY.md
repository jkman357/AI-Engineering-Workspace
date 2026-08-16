# Security and Privacy Notes

## Scope

AI Engineering Workspace is an RC-stage engineering workspace. Security properties described here are design boundaries and current implementation behavior, not certification or a claim of suitability for a regulated or high-assurance environment.

## Credential handling

AI Engineering Workspace does **not intentionally provide, implement, or maintain credential/password storage** and is not designed to collect or manage user authentication credentials.

The application does not implement its own:

- password vault;
- credential database;
- account database;
- authentication provider.

Firefox remains responsible for browser-managed cookies, sessions, saved logins, and password-manager behavior selected by the user.

The current Workspace design does not require reading Firefox saved-login databases and does not intentionally access or maintain browser password-manager data.

This is design-intent wording, not an absolute guarantee that arbitrary operating-system, browser, third-party, exception, clipboard, command-line, URL, or diagnostic data can never contain sensitive text.

## Firefox / browser profile trust boundary

The browser profile is outside the Workspace's credential-storage responsibility:

```text
AI Engineering Workspace
        |
        | HWND hosting / controlled UI interaction
        v
Firefox process
        |
        v
Firefox profile
├─ cookies
├─ sessions
├─ saved logins
└─ browser settings
```

Important implications:

- a docked Firefox window may already represent an authenticated session;
- the Workspace can focus and issue controlled UI navigation to the specific docked HWND;
- future message/context routing may increase the consequences of controlling an authenticated browser session;
- workstation access, browser-profile security, screen/clipboard exposure, and operating-system endpoint controls remain relevant trust assumptions.

## Workspace project file boundary

`.aew` Workspace project files are local JSON documents used to restore UI/workspace state. The current schema may contain local filesystem paths, pane aliases/PaneIds, pane geometry, layout mode, main-window geometry/state, and the Show IDs preference.

The current schema intentionally does **not** persist Browser current URLs, browsing history, cookies, login/session state, password-manager data, account credentials, or passwords. Restored Browser panes are endpoint/layout definitions and start Firefox at the configured default startup URL (Google) when launched.

A Workspace project can still reveal local path names and project/workstation structure. Treat `.aew` files as local engineering metadata and review them before publishing or sharing outside the intended environment.

If a saved File path is unavailable at load time, the application falls back that pane to the user's Desktop rather than creating the missing directory or failing the entire project.

## Diagnostics

Runtime logs are local engineering diagnostics and are not automatically uploaded by the application.

Normal source-checkout location:

```text
logs\runtime\AIEngineeringWorkspace_*.log
```

Fallback when a repository root cannot be found:

```text
%LOCALAPPDATA%\AIEngineeringWorkspace\logs\runtime\
```

Default application-runtime log policy:

- retention: 14 days;
- maximum retained files: 50;
- rotation threshold: 10 MB;
- best-effort sensitive-value redaction for common password/token/Authorization patterns and sensitive URL query parameters.

Configuration:

```text
AIEW_RUNTIME_LOG=0          Disable application runtime logging
AIEW_LOG_RETENTION_DAYS=14  Retention age
AIEW_LOG_MAX_FILES=50       Maximum retained files
AIEW_LOG_MAX_MB=10          Rotation threshold per file
```

Best-effort redaction is not guaranteed to recognize every secret format. Logs may contain:

- browser URLs;
- local file/folder paths and file names;
- PID/HWND values;
- pane aliases and PaneId values;
- exception/error details;
- other engineering context included in diagnostic messages.

Review and sanitize diagnostics before sharing them outside the intended environment.

Build/launcher logs produced by command scripts are separate from the application's runtime retention/rotation mechanism.

## Privilege model

The manifest uses `asInvoker`. The application is designed to work as a standard user and does not require Administrator elevation by design.

`asInvoker` follows the caller's token. If the parent context is already elevated, the process may inherit elevated privileges. Therefore the project does not claim that it “always runs without Administrator privileges.”

The application and scripts do not intentionally bypass Windows, enterprise endpoint-management, execution-policy, or IT security controls.

## Browser lifecycle boundary

Normal Browser-pane lifecycle operations target the exact HWND/PID that the Workspace has identified. Workspace-owned windows are closed gracefully with a window-close request rather than broad process termination. Firefox windows attached through `Dock Existing` are restored instead of intentionally terminated by normal Workspace shutdown ownership rules.

## Reporting

Do not place passwords, authentication tokens, private credentials, confidential local paths, sensitive screenshots, or production data into public GitHub issues.

## Windows Shell extension trust boundary

File-pane right-click menus in `v0.0.6rc06` are obtained from Windows Shell through `IContextMenu`. Registered Shell extensions may therefore contribute menu commands and may execute third-party code in the Workspace process context when Windows activates those extensions.

Examples can include 7-Zip, TortoiseGit, comparison tools, security products, cloud-storage clients, and other Explorer integrations installed on the workstation. Their behavior, privilege model, data handling, updates, and security properties are outside the control of AI Engineering Workspace.

The Workspace does not silently invoke a Shell command: execution follows an explicit user menu selection. Organizations evaluating the tool should include installed Shell extensions in their endpoint trust assessment.

Git status badges are read-only probes using local `git.exe` commands (`rev-parse`, `status --porcelain`, and `ls-files`). They do not commit, push, pull, or modify repository content.
