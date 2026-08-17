# Security review

Review date: 2026-08-17

## Threat model

The widget runs in the same Windows user session as Codex and therefore can read that user's Codex access token. The design goal is to minimize where that token can travel and to avoid persistence, privilege escalation, or executable update paths.

## Enforced controls

- Runs at the current user's privilege level through an `asInvoker` manifest.
- Contains one fixed remote business endpoint: `https://chatgpt.com/backend-api/wham/usage`.
- Disables HTTP redirects and cookies for the credentialed client.
- Sends the Bearer token only in a request created for that fixed endpoint.
- Reads only `tokens.access_token` and `tokens.account_id`; it does not read or use `refresh_token`.
- Does not cache credentials, write them to logs, or include them in error messages.
- Limits `auth.json` to 256 KiB, clears temporary raw credential buffers after parsing, and limits local settings input to 64 KiB.
- Limits the response body to 1 MiB before JSON parsing.
- Never modifies `auth.json`, Codex configuration, hooks, browser data, the Codex installation, or Windows security settings.
- When `Follow Codex` is enabled, writes exactly one current-user Run value containing this executable path and the fixed `--background` argument. Disabling the setting removes that value.
- Checks local process metadata every two seconds for the exact packaged path suffix `WindowsApps\OpenAI.Codex_*\app\ChatGPT.exe`; it does not open process memory, enable CDP, inject code, send window messages, or modify the Codex process.
- Uses Windows UI Automation every 300 ms only to read visible composer-button names, CSS class tokens exposed by Chromium accessibility, and screen bounds. It binds its own tool window as an owned window and repositions that window with `SetWindowPos`; it never invokes an automation pattern or sends input to Codex.
- Samples one screen pixel immediately above the footer to choose light or dark text. The pixel is used only for an in-memory luminance calculation and is not stored or transmitted.
- Requires a valid event timestamp before accepting a local rate-limit record and discards quota windows whose reset time has already passed.
- Has no updater, plugin loader, embedded browser, scripting engine, or third-party runtime package in the application project.
- Local settings contain opacity, click-through, follow-Codex, and legacy window-position/topmost/position-lock values retained for settings-file compatibility. The attached layout does not use or update the legacy position/topmost fields.
- Invalid or unreadable settings do not trigger Run-key synchronization and are not overwritten automatically on exit; an explicit settings action is required before writing a replacement configuration.

## Expected network traffic

One HTTPS `GET` request every 60 seconds while the widget is actively shown or running independently. A manual refresh or one debounced refresh after a burst of local session-file changes may issue an additional request. In follow mode, closing Codex hides the widget, disables session monitoring, cancels the active request, and stops quota traffic; the local process check continues without network access. If a request fails, the widget reads recent local Codex session JSONL files and does not contact a fallback service.

## Residual risk

- Any process that can read the user's files can potentially read the same Codex credentials. Protect the Windows account and do not run untrusted binaries as that user.
- The usage URL is an internal product endpoint rather than a stable public API and can change without notice.
- Release archives are not Authenticode-signed. Verify the published SHA-256 before use.
- The Codex process check is a path heuristic used only to show or hide the widget, not a security identity boundary.
- The bottom-bar anchors are internal accessibility metadata rather than a public Codex API. A Codex UI update can make the locator stop matching; the widget fails closed by hiding instead of guessing another screen position.
- A compromised source tree or build machine can still produce a malicious binary; reproducible review of the source and release hash remains necessary.

## Verification performed

- Release build completed with no compiler warnings or errors.
- Unit tests cover local session parsing and timestamp validity, expired quota periods, online response parsing, refresh ordering and cancellation, malformed external JSON and credentials, process-path matching, chunked response size limits, official remaining-percentage conversion, and safe placement between composer anchors.
- A live probe successfully fetched the quota windows currently available to the account using the same client as the widget.
- NuGet vulnerability scan reported no known vulnerable direct or transitive packages.
- Static endpoint and sensitive-operation scan found no additional application network destinations, token refresh logic, browser-cookie access, process spawning, Codex-process access, or auth-file writes. The sole registry write is the documented per-user Run value used by follow mode.
