# Security review

Review date: 2026-07-12

## Threat model

The widget runs in the same Windows user session as Codex and therefore can read that user's Codex access token. The design goal is to minimize where that token can travel and to avoid persistence, privilege escalation, or executable update paths.

## Enforced controls

- Runs at the current user's privilege level through an `asInvoker` manifest.
- Contains one fixed remote business endpoint: `https://chatgpt.com/backend-api/wham/usage`.
- Disables HTTP redirects and cookies for the credentialed client.
- Sends the Bearer token only in a request created for that fixed endpoint.
- Reads only `tokens.access_token` and `tokens.account_id`; it does not read or use `refresh_token`.
- Does not cache credentials, write them to logs, or include them in error messages.
- Limits the response body to 1 MiB before JSON parsing.
- Never modifies `auth.json`, Codex configuration, hooks, browser data, startup entries, or Windows security settings.
- Has no updater, plugin loader, embedded browser, scripting engine, or third-party runtime package in the application project.
- Local settings contain only window position, opacity, topmost, position-lock, and click-through values.

## Expected network traffic

One HTTPS `GET` request every 60 seconds while the widget is running. A manual tray refresh may issue an additional request. If the request fails, the widget reads recent local Codex session JSONL files and does not contact a fallback service.

## Residual risk

- Any process that can read the user's files can potentially read the same Codex credentials. Protect the Windows account and do not run untrusted binaries as that user.
- The usage URL is an internal product endpoint rather than a stable public API and can change without notice.
- Release archives are not Authenticode-signed. Verify the published SHA-256 before use.
- A compromised source tree or build machine can still produce a malicious binary; reproducible review of the source and release hash remains necessary.

## Verification performed

- Release build completed with no compiler warnings or errors.
- Unit tests cover local session parsing, online response parsing, and the official remaining-percentage conversion.
- A live probe successfully fetched both expected quota windows using the same client as the widget.
- NuGet vulnerability scan reported no known vulnerable direct or transitive packages.
- Static endpoint and sensitive-operation scan found no additional application network destinations, token refresh logic, browser-cookie access, process spawning, registry writes, or auth-file writes.
