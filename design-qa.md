# Design QA

- Source visual truth: the live Codex composer footer supplied by the user on 2026-08-17.
- Implementation evidence: `screenshots\implementation-v12-inline-footer.png`.
- Viewport: 72 x 28 for one available quota window; 148 x 28 for two.
- State: live weekly quota, Codex light theme, attached between the permissions and model controls.
- Full-view comparison evidence: the live implementation was captured from the actual Codex desktop window after the quota bar attached to the composer footer.
- Focused region comparison: the permission control, quota bar, context-usage ring, model name, dictation button, and submit button were captured on the same horizontal strip.
- Regression previews now use fixed in-process quota fixtures. They do not read real settings or credentials, contact the usage endpoint, create a tray icon, start timers/watchers, or write the registry. Capture waits for layout and a 750 ms DWM composition window to avoid incomplete acrylic frames.

## Findings

No actionable P0, P1, or P2 differences remain in the attached layout.

- Fonts and typography: Segoe UI Variable Text and Microsoft YaHei UI match the surrounding Codex controls at 11px; reset details remain available in the tooltip instead of expanding the footer.
- Spacing and layout rhythm: the quota bar is geometrically centered in the free space between the permissions and model buttons. A user-reviewed 2px downward optical correction aligns the quota text baseline with both native controls.
- Colors and visual tokens: the weekly marker is a 10px green outline ring matching the neighboring context-usage ring silhouette. Text and separator colors switch from a one-pixel local luminance sample without retaining the sampled pixel.
- Image quality and asset fidelity: the selected target contains no logos, illustrations, photographs, or non-standard image assets. No placeholder or code-drawn visual asset replaces source imagery.
- Copy and content: the implementation retains `Codex`, `5 小时`, `周额度`, remaining percentages, exact local reset timestamps, and live state. Percentage differences between captures are expected because the implementation displays live quota data.

## Comparison history

### Pass 1

- Evidence: `screenshots\implementation-v1.png`
- Finding: the compact information hierarchy was correct, but the off-screen WPF render could not show the desktop backdrop through the acrylic surface.
- Action: replaced the off-screen-only QA capture with a controlled native screen capture over a known two-tone backdrop.

### Pass 2

- Evidence: `screenshots\implementation-v2.png`
- P2: the combined acrylic and WPF surface tints made the widget read too close to an opaque black panel.
- P2: reset copy and percentages were slightly under-scaled compared with the visual target.
- Fixes: reduced the WPF surface alpha from `B8` to `42`, set the native acrylic tint to `A0`, increased reset copy to 10px, row labels to 12px, and percentages to 28px.

### Pass 3

- Evidence: `screenshots\implementation-v3.png`
- Result: backdrop variation is visible through the window, the single-surface hierarchy remains intact, and all quota text is readable at 324 x 176. No P0/P1/P2 fixes remain.

### Pass 4

- Evidence: `screenshots\implementation-v4-weekly-bar.png` and the user's real-desktop capture showing the missing weekly bar.
- P1: the two fixed 61px quota rows plus border and margins exceeded the final content height by 2px, clipping the weekly progress rule at the bottom edge.
- Fix: reduced both quota rows to 58px, preserving the 324 x 176 window while adding four pixels of bottom safety space.
- Result: both progress rules are fully visible in the same live state and remain aligned with their corresponding quota rows.

### Pass 5

- Evidence: `screenshots\implementation-v5-glass-corners.png` over a controlled blue-gray and wine-red backdrop.
- P1: WPF rounded geometry, a WPF rounded shadow, and DWM corner preference produced competing corner contours.
- P1: the native acrylic tint and WPF surface tint combined into an almost opaque panel on a real desktop.
- Fixes: removed DWM corner clipping and the WPF shadow, retained one WPF rounded border, reduced the acrylic tint to 50%, and reduced the WPF surface tint to approximately 8%.
- Result: one corner contour remains; both controlled backdrop colors visibly diffuse through the window while quota text retains high contrast.

### Pass 6

- Evidence: `screenshots\implementation-v6-native-corners.png` and the user's real-desktop capture showing a rectangular acrylic HWND behind the rounded WPF border.
- P1: removing DWM corner clipping left the acrylic composition rectangular even though the inner content border looked rounded.
- Fix: restored `DWMWA_WINDOW_CORNER_PREFERENCE` at the native HWND level and removed WPF corner radius, border thickness, and border brush entirely.
- Result: DWM now clips the complete acrylic window; corner pixels show the controlled backdrop outside the native rounded region, with no second WPF contour.

### Pass 7

- Evidence: `screenshots\implementation-v10-final.png` (final labels) and `implementation-v8-compact-rings.png` (initial compact pass).
- Change: replaced the 324 x 176 vertical meter layout with a 286 x 112 horizontal two-column layout and code-native 48px circular progress rings.
- Result: 5-hour and weekly quotas are visible in one glance; both exact reset timestamps fit without truncation; true DWM acrylic corners remain intact.

### Pass 8

- Evidence: `screenshots\implementation-v9-themed-menu.png`.
- P2: the prior WPF context menu inherited a white native-default appearance that conflicted with the acrylic widget.
- Fix: added a custom dark rounded menu surface, consistent typography, mint check states, compact spacing, subtle separators, and a single-click opacity cycle to avoid a bulky submenu.
- Result: the context menu reads as part of the same product surface and remains legible over both controlled backdrop tones.

### Pass 9

- Evidence: `screenshots\implementation-v11-ultra-compact.png`.
- Change: reduced the viewport from 286 x 112 to 238 x 100, rings from 48px to 42px, and tightened column gaps while preserving the two-column scan path.
- Result: the widget is 17% narrower and 11% shorter. Both percentages, labels, and exact reset timestamps remain visible; the weekly line omits redundant `重置` copy to preserve a clean right edge.

## Follow-up polish

- P3: a hover-state capture for tray/drag affordance is not included because the selected visual deliberately hides persistent window controls.

### Pass 10

- Evidence: the user's first live attached-footer screenshot.
- Change: replaced the 238 x 100 desktop card with a 72/148 x 28 transparent owned window located from the live permissions and model-button accessibility bounds.
- P2: the quota text appeared optically high even though the bounding rectangles were mathematically centered.
- Fix: shifted the attached strip down by 2px.

### Pass 11

- Evidence: the user's follow-up screenshot with the complete green outline ring.
- Change: replaced the weekly 5px filled dot with a 10px green outline ring matching the native context-usage indicator.
- P2: the outline was always complete and therefore did not encode the displayed 49% quota.

### Pass 12

- Evidence: `screenshots\implementation-v12-inline-footer.png`.
- Fix: reused the code-native circular progress renderer at 10px with a 1.5px green remaining-quota arc over a low-contrast track.
- Result: the ring now visibly represents 49%, while permission text, quota text, context indicator, and model text retain one visual baseline. Cached accessibility anchors reduced a five-second CPU sample from 0.922s to 0.141s without changing the 300ms follow cadence.

### Pass 13

- Evidence: two Windows `MoAppHangXProc` reports on 2026-08-17 named `CodexQuotaWidget.exe` as the external process after the attached-footer build was enabled.
- P0: synchronous 300ms accessibility reads on the WPF dispatcher combined with a cross-process owned-window relationship created a possible circular wait between Codex and the widget.
- Fix: removed cross-process ownership, moved UI Automation to a background single-flight probe limited to once per five seconds, batch-cached automation properties, hid after a one-second timeout, projected window-only movement with native bounds, and skipped unchanged `SetWindowPos` calls.
- Result: the quota strip remains visually independent from the Codex DOM and process. A stalled accessibility provider can hide one overlay and occupy at most one background probe; it can no longer block the widget dispatcher while Codex waits on an owned window.

final result: passed
