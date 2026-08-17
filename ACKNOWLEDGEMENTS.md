# Acknowledgements

The product direction was informed by the open-source projects
[CodexBar](https://github.com/steipete/CodexBar),
[Win-CodexBar](https://github.com/Finesssee/Win-CodexBar), and
[codex-usage-widget](https://github.com/WeikangLin93/codex-usage-widget).

The composer-relative display concept was also informed by
[Cockpit Tools](https://github.com/jlcodes99/cockpit-tools). Cockpit Tools uses
loopback CDP and renderer JavaScript for its client overlay; CodexQuotaWidget
does not copy that implementation and instead uses an independently written,
read-only Windows UI Automation locator plus an external owned window.

CodexQuotaWidget is a new, narrow Windows implementation focused on one provider and a smaller credential/network surface. No upstream binary components are bundled.
