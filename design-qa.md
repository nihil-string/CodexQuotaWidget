# Design QA

- Source visual truth: `docs\design\selected-compact-glass-target.png`
- Implementation screenshot: `C:\Users\myozi\Desktop\CodexQuotaWidget\screenshots\implementation-v6-native-corners.png`
- Viewport: 324 x 176 device-independent pixels
- State: live online quota state, dark acrylic backdrop, no hover
- Full-view comparison evidence: the source and implementation were opened together after each capture; the implementation preserves the selected vertical two-row hierarchy while intentionally compressing the generated mock to the user's requested floating-widget footprint.
- Focused region comparison: not required. The entire implementation is 324 x 176 and all typography, borders, progress rules, and copy remain legible in the full-resolution capture.

## Findings

No actionable P0, P1, or P2 differences remain.

- Fonts and typography: Bahnschrift is used for the display numerals and product label; Microsoft YaHei UI is used for Chinese labels and reset copy. The final pass increased reset copy to 10px and quota numerals to 28px for legibility at the compact viewport.
- Spacing and layout rhythm: 17px horizontal padding, 13px top padding, two 61px quota rows, and single-pixel separators reproduce the target hierarchy without the target mock's excessive vertical canvas.
- Colors and visual tokens: neutral warm-white text, cool gray secondary copy, one muted mint live indicator, and monochrome progress rules match the selected direction. Native Windows acrylic uses a 63% charcoal tint beneath a 26% WPF surface tint so the controlled backdrop remains perceptible.
- Image quality and asset fidelity: the selected target contains no logos, illustrations, photographs, or non-standard image assets. No placeholder or code-drawn visual asset replaces source imagery.
- Copy and content: the implementation retains `Codex`, `5 小时`, `每周`, remaining percentages, reset times, and live state. Percentage differences between the mock and capture are expected because the implementation displays live quota data.

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

## Follow-up polish

- P3: a hover-state capture for tray/drag affordance is not included because the selected visual deliberately hides persistent window controls.

final result: passed
