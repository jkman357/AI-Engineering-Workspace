# Security and Privacy Notes

## Credentials

AI Engineering Workspace does **not** store, collect, persist, or manage user account credentials or passwords.

The application does not provide a password vault, credential database, account database, or authentication provider. Browser authentication state remains managed by Firefox and its user profile, including cookies, sessions, and any browser password-manager behavior selected by the user. AI Engineering Workspace does not implement credential capture or persistence and does not intentionally access or maintain browser password-manager data.

## Diagnostics

Runtime logs are engineering diagnostics. They may include:

- browser URLs supplied to launch operations
- local file/folder paths
- process IDs
- HWND values
- pane aliases and PaneId values
- exception/error details

Logs should therefore be handled according to the user's own organizational and privacy requirements.

## Privilege model

The application is designed for standard-user execution (`asInvoker`). It does not intentionally bypass Windows, enterprise endpoint-management, or IT security policy.

## Reporting

Do not place passwords, authentication tokens, private credentials, or sensitive production data into public GitHub issues when reporting a problem.
