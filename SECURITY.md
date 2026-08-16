# Security and Privacy Notes

## Scope

AI Engineering Workspace is RC-stage engineering software. Security properties below are implementation boundaries and design intent, not certification or a suitability claim for regulated/high-assurance use.

## Credential handling

AI Engineering Workspace does **not intentionally provide, implement, persist, collect, or manage credential/password storage** and is not designed to collect or manage user authentication credentials.

The application does not implement its own password vault, credential database, account database, or authentication provider. Firefox remains responsible for browser-managed cookies, sessions, saved logins, and password-manager behavior selected by the user. The Workspace does not intentionally access or maintain Firefox password-manager databases.

## Firefox / browser profile trust boundary

A docked Firefox window may already represent an authenticated session. The Workspace hosts the Firefox HWND and can perform controlled UI focus/navigation operations, but Firefox profile/session storage remains outside the Workspace.

## Workspace project boundary

`.aew` files may contain local filesystem paths, pane aliases/PaneIds, pane geometry, layout mode, window state, and Show IDs preference. They intentionally do **not** persist Browser current URLs, browsing history, cookies, login/session state, password-manager data, account credentials, or passwords.

## Diagnostics

Runtime logs are local and are not automatically uploaded. Normal source-checkout location:

```text
logs\runtime\AIEngineeringWorkspace_*.log
```

Fallback:

```text
%LOCALAPPDATA%\AIEngineeringWorkspace\logs\runtime\
```

Defaults: 14-day retention, 50 files, 10 MB rotation, and best-effort sensitive-value redaction. Redaction cannot guarantee recognition of every secret format. Review and sanitize logs before sharing.

Environment variables:

```text
AIEW_RUNTIME_LOG=0
AIEW_LOG_RETENTION_DAYS=14
AIEW_LOG_MAX_FILES=50
AIEW_LOG_MAX_MB=10
```

## Privilege model

The manifest uses `asInvoker`. The application is designed for standard-user operation and does not require Administrator elevation by design. `asInvoker` follows the caller's token; an already elevated parent can still launch an elevated process.

## Firefox lifecycle boundary

Browser lifecycle operations target the exact HWND/PID identified by the Workspace. Workspace-launched Firefox windows are closed by graceful window-close requests after identity validation. `Dock Existing` windows are restored rather than broadly terminating Firefox processes.

## Windows Shell extension trust boundary

File-pane right-click menus are obtained through Windows Shell `IContextMenu`. Registered extensions such as 7-Zip, TortoiseGit, compare tools, endpoint-security products, and cloud clients may execute third-party code in the Workspace process when the user selects their commands. Their security, privilege, update, and data-handling behavior is outside this project.

Git status badges are read-only probes using local `git.exe` commands and do not commit, push, pull, or modify repository content.

## Reporting

Do not place passwords, tokens, private credentials, confidential paths, sensitive screenshots, or production data into public issues.
