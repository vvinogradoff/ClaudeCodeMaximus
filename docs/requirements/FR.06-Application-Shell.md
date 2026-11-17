# FR.6 — Application Shell (batch 2026-11-17)

> **Staging file, phase 1.** New and amended text only. Use cases: [UC.SH.01](/docs/usecases/UC.SH.01-Window-Chrome.md). Test cases: [TC.SH.01](/docs/testcases/TC.SH.01-Window-Chrome.md). New IDs start at **FR.6.5**.

## Amendments to existing requirements

| Existing | Amended by | Change |
|---|---|---|
| FR.6.3 Title bar toolbar | FR.6.7 | Chevron, hamburger and day/night stay. The session instruction toolbar (FR.11) now lives in the title bar too. The hamburger lists Import, Settings and Exit. |
| FR.6.4 Window controls and draggable title bar | FR.6.6 | Non-interactive text in the title bar (the version) is not focusable and does not stop a drag. |

## New requirements

**FR.6.5 — Application icon.** The window, the taskbar button, the tray icon (background host) and the executable use **one icon: the Tessyn Web app icon** (`code/Tessyn.Web/ClientApp/public/icon.svg`, rendered to the sizes Windows needs, 16 to 256 px). The old Avalonia logo is not used by the WPF client. The same artwork serves the web (`icon-192`, `icon-512`) and the desktop, so the product looks one across clients.

**FR.6.6 — Version text.** The version text is a plain label: **not focusable**, no tab stop, no focus rectangle, no selection. A press on it starts a window **drag**, as a press on the empty title bar does (FR.6.4); a double-click maximises or restores.

**FR.6.7 — Title bar contents (WPF).**
- Left to right: chevron toggle, hamburger, day/night, the **instruction toolbar** (icon buttons, each with its text as the tooltip: Auto-Commit cycling, New branch, Document, Compact, Compact now, Terminate; a one-line notice after them, full text in its tooltip), the **Recent dropdown** filling the middle, then the window controls. A 48 px gap right of the dropdown stays free for dragging.
- The **hamburger menu** lists, in order, the registered entries (Import, then Settings), a separator and Exit.
- The dropdown button shows the **open session's name** ("Recent" when none is open); its popup is as wide as the button.

**FR.6.8 — Status bar (WPF).** The two plan-usage meters (FR.18) fill the bar's full height and take all the width left by the other items. The Profiles button is at the far right.
