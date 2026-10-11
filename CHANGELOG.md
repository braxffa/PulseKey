# Changelog

## v1.0.4 — 2026-10-11

- Updated the Repeat actions-per-second estimate as the interval is typed and applied a committed interval to an armed Repeat action.
- Made selected page, input, behavior, theme, and dropdown choices stand out across all 15 themes.

## v1.0.3 — 2026-10-11

- Repainted the full interface when resizing to clear dark blocks behind the sidebar.
- Reduced the minimum window size to 680×480 and scaled the controls to fit.

## v1.0.2 — 2026-10-10

- Made the whole interface scale with a maximized or full-screen window: working cards, buttons, fields, macro timeline, theme tiles, text, sidebar, and header.
- Restored the original control sizes when leaving full screen or returning to the normal window size.
- Kept the scaled workspace centered and reduced needless redraws by updating the scale in small increments during resizing.
- Added full-screen screenshots for the theme picker and a Shift macro step.

## v1.0.1 — 2026-10-10

- Added F11 and a visible button for true borderless full screen, with restoration of the prior window state.
- Centered the working cards when maximized or in full screen instead of stretching them and leaving their controls at one edge.
- Replaced the full-width gradient bitmap with an eight-pixel-wide tiled strip to keep full-screen resize light and preserve color continuity.
- Allowed Shift, Ctrl, and Alt in the key picker; macro steps display friendly modifier names while preserving Windows Forms key values in XML.

## v1.0.0 — 2026-10-10

First public PulseKey release by [@BraxFFA](https://linktr.ee/BraxFFA).

- Portable 64-bit Windows Forms application with Quick action, Macro studio, and Appearance pages.
- Hold or repeat one keyboard key or mouse button, with adjustable repeat timing.
- Visible-window target picker, foreground-only operation, and F8/F9 controls.
- Idle, Armed, and Active states with theme-matched colors.
- Manual macro timeline with key, click, and wait steps; edit, reorder, save, load, and run once.
- Fifteen dark themes with smooth full-window gradients, paired accents, and a true-black option.
- Themed clickable author credit pointing to `https://linktr.ee/BraxFFA`.
- Full source, build instructions, screenshots, portable package, standalone executable, and checksums.
