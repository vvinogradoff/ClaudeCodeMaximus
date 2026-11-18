> Area file of the requirements (index: [README](README.md); old IDs and where they went: [ID-MAP](ID-MAP.md)). The text below is the unchanged existing text of this area; the batch of 2026-11-17 is at the end.

### FR.9 — Theme & Color Customization

**FR.9.1 — Theme variants:** The application supports Dark and Light themes. The selected theme controls the UI framework's theme variant (which affects all built-in control styling) and also selects which set of custom colors to apply.

**FR.9.2 — Per-theme custom colors:** Each theme (Dark and Light) has an independent set of customizable colors stored as hex strings in `appsettings.json`:
- Input box background and text color
- User message bubble background and text color
- Code block background and text color
- Inline code background and text color
- System message bubble background color
- Session recency bar colors (3 tiers: 15 min, 30 min, 60 min)

**FR.9.3 — Color application:** Custom colors are applied as application-level theme resources (named `Cmx*` colour keys: `CmxInputBg`, `CmxInputFg`, `CmxUserBubbleBg`, `CmxUserBubbleFg`, `CmxCodeBg`, `CmxCodeFg`, `CmxInlineCodeBg`, `CmxInlineCodeFg`, `CmxSystemBubbleBg`, `CmxRecency15Min`, `CmxRecency30Min`, `CmxRecency60Min`). These are consumed by the session view, the markdown/transcript renderer and the session-tree rows (recency colours). The editable colour palette is the **Colours tab** of FR.9.9 (decision A8 is withdrawn). Colors are re-applied immediately when the theme is toggled or when settings are saved.

**FR.9.5 — Session recency bars:** Session nodes in the tree display a colored background bar indicating how recently the last user prompt was sent:
- **Light green** (customizable): last prompt within 15 minutes
- **Green** (customizable): last prompt within 30 minutes
- **Dark green** (customizable): last prompt within 1 hour
- No bar: last prompt more than 1 hour ago (or no prompts)

Recency bars refresh automatically every 60 seconds so the visual state stays current as time passes. The three recency colors are per-theme and editable in the Settings window.

> **Superseded 2026-11-17 by FR.9.10** (four tiers on the latest activity, plus the 24-hour dotted border). The FR.9.5 text above is kept for the record.

**FR.9.4 — Sensible defaults:** Both themes ship with sensible default color values. Dark defaults use VS Code-inspired dark palette; Light defaults use standard light-background colors. Users can customize any color via hex input in the Settings window.

**FR.9.6 — Recent-dropdown hide:** The top-bar recent-sessions dropdown lists sessions active within the last 24 hours, plus Pinned Sessions (FR.9.8). Each row of a session that is not pinned has a hide button that sets `RecentIgnoredAt` (`SessionNodeModel`) to the current time, removing the session from the dropdown until its next real activity (prompt, response, or draft edit) — tracked via `IsHiddenFromRecent` on `SessionNodeViewModel`, which auto-clears once newer activity is recorded.

**FR.9.7 — Favorite sessions:** A session can be marked as a favorite, independent of the FR.9.6 hide/recency mechanism.
- **Persistence:** `IsFavorite` (bool) on `SessionNodeModel`, persisted in `appsettings.json`.
- **Command-bar toggle:** A star toggle button in the session command bar (FR.12.2), immediately left of the Model selector. Unchecked: grey-stroked, transparent fill. Checked: black-stroked, gold/yellow fill. Clicking toggles favorite state for the active session.
- **Recent dropdown inclusion:** A favorite is a Pinned Session (FR.9.8): it always remains in the top-bar recent-sessions dropdown regardless of the 24-hour recency window (FR.9.6) and regardless of hidden state. Sort order is unchanged — most-recently-active first — so a favorite with no recent activity sinks to the bottom of the list rather than being dropped.
- **Recent-dropdown row affordance:** Each dropdown row shows at most one icon on its right side: a gold/black star if the session is favorited (clicking un-favorites it, after which the hide icon from FR.9.6 takes its place unless the session is still pinned by FR.9.8), or the hide icon (⦸) if the session is not pinned (clicking hides it per FR.9.6). A non-favorite session pinned by open Focus items or schedules shows neither icon; the icon slot keeps its width so the row's columns stay aligned.
- **Tree and Recent-tab display:** The session tree and the Recent-tab flat list each show a display-only gold/black star at the far right of the session row's top line, above the last-activity date/time, when the session is favorited. The existing draft indicator (✏, FR — "has unsent text") remains immediately right of the session name and is unaffected.
- **Hide icon replacement:** The recent-dropdown hide icon (FR.9.6) is changed from the eye pictogram (👁) to a monochrome bold-slashed-circle glyph (⦸, U+29B8), for visual consistency with the rest of the flat, non-emoji iconography used elsewhere in the command bar.

**FR.9.8 — Recent-dropdown Pinned Sessions and order:**
- **Pinned:** a session is pinned in the top-bar recent-sessions dropdown when it is a favorite (FR.9.7), has at least one open Focus item of any kind, `Decided` included (FR.23.17), or has at least one schedule (FR.14).
- **Membership:** the dropdown lists every Pinned Session regardless of the 24-hour recency window and of hidden state (`RecentIgnoredAt`, FR.9.6), plus every session active within the last 24 hours that is not hidden. A pinned row offers no hide button, since hiding it would have no visible effect. Worker Sessions, shown nested under their supervisor in the tree, are not listed. The rule lives in `RecentSessionVisibilityService` (`ShouldShowInRecentDropdown`, `IsPinnedInRecentDropdown`).
- **Order:** most recently active first, by the latest of the last prompt sent, the last response received and the last non-empty draft edit (`SessionNodeViewModel.LastActivityTimestamp`). Sessions with no activity come last.
- **Refresh:** the list, and each session's schedules flag, are rebuilt at startup, every time the dropdown opens and after a hide. Row badges (FR.23.17) update live, also while the list is open.
- **Row badge:** each row shows the session-row badge (FR.23.17) between the status dots and the session name. The closed dropdown box shows the session name only.

---

---

## Amendments and additions (batch 2026-11-17)

### Amendments to existing requirements

| Existing | Amended by | Change |
|---|---|---|
| FR.9.2 Per-theme custom colours | FR.9.9 | The list of editable colours grows from 12 to the full palette; **every** colour has a Light and a Dark value. |
| FR.9.3 Colour application | FR.9.9 | The sentence "the editable colour palette is dropped at WPF parity" is **withdrawn**: the Colours tab is that editor (it replaces decision A8). |
| FR.9.4 Sensible defaults | FR.9.9 | Defaults are the colours of the catalogue (FR.9.9), separately for Light and Dark. |
| **FR.9.5 Session recency bars** | **FR.9.10** | **Replaced.** Three tiers (15/30/60 min, last prompt) become four tiers on the latest activity, plus the dotted border. FR.9.5 stays as an ID and points to FR.9.10. |
| FR.9.6 Recent-dropdown hide | FR.9.11 | A row keeps its recency colour in every state of the dropdown. |
| FR.9.8 Pinned sessions and order | FR.1.20 | Row content and layout in the dropdown. |
| FR.9.13 Recency colour on the closed Recent dropdown | FR.9.15 | The closed button keeps its recency colour while hovered and while open: a faint overlay, never a replaced background. |
| FR.9.9 Colours tab | FR.9.16 | Two new palette entries: the active workers badge's number and outline. |
| FR.9.9 Colours tab | FR.9.18 | An **Apply** button; less space above the list; the colours in **two columns**; a gap before the scrollbar. |
| FR.9.9 Colours tab (Live), FR.9.18 Apply | FR.9.19 | The picker no longer applies a colour on every pointer move: picked colours are **pending** until **Apply** (or Save). The picker gains the **named web colours** and **colour harmonies**. |

### New requirements

**FR.9.9 — Colours tab (WPF).**
- Settings has a **Colours** tab (section `colours` in the client state).
- **Theme.** The tab edits one theme at a time, **Light** or **Dark**, chosen by a switch at its top (it starts on the active theme). Every colour has **its own value per theme**; changing one theme never changes the other. Values are saved **per theme**.
- **Scope.** The palette covers every themed colour of the client, in groups: *Session output* (input box background and text, user bubble, code block, inline code, **system rows** `SystemRow`, automated input teal), *Session lists* (the four recency tiers, the 24-hour border, running and resumable squares, the selected row), *Focus* (kinds Critical, Question, Todo, Alert, Decided: background and text each) and *Window* (accent, button, toggle-on states, title bar checked state).
- **Colour picker.** Each row shows a **swatch** that opens a **colour picker** (hue, saturation and brightness area, an opacity slider, previews of the old and the new colour). **The hex text is not shown** in the row and not typed by the user; the picker is the only way to edit. A **Reset** per row returns that colour to the theme's default, and **Reset all** returns every colour of the theme shown.
- **Storage.** A value equal to the theme's default is not stored. A theme never inherits from the other theme.
- **Defaults** are: Green, LightGreen, HoneyDew, MintCream, DarkGreen, DarkRed, DodgerBlue, `#8B0000`, `#DAA520`, `#707070` and the pale blue `#CDE6F7` (dark `#264F78`) for the selected row; Todo and Alert follow the theme; each has its light and dark default.
- **Live.** Changing a colour, or the theme, shows at once everywhere (brushes are dynamic resources; the transcript page gets the new palette in its `appearance` message).
- The Focus chips and the count pill in lists use the same palette keys.

**FR.9.10 — Session recency (replaces FR.9.5).** A session row has a background (its *recency box*) by the **latest activity**: the last prompt sent, the last response received, or the last non-empty draft edit (the same instant as FR.9.8's order).

| Latest activity within | Colour (palette key; default) |
|---|---|
| 15 minutes | Green |
| 1 hour | LightGreen |
| 4 hours | HoneyDew |
| 12 hours | MintCream |
| older, or none | none |

- A session with **unsent draft text** keeps at least the palest tier (MintCream).
- **24-hour border.** A session with activity in the last 24 hours also has a **dotted dark-green border**.
- The tiers, the border and the date text refresh every **60 seconds**.
- All five colours are per-theme palette keys (FR.9.9). The four tiers are the same in the tree, the Recent tab, the Tags tab and the Recent dropdown.

**FR.9.11 — The Recent dropdown keeps its colours when open.** The dropdown's header button is **flat**: no accent fill and no dark-blue checked state while the popup is open (only a faint hover or open tint). The rows in the popup are flat buttons with only a faint hover overlay over the row's **own recency colour**, so a row looks the same open, hovered or pressed.


**FR.9.12 — Line spacing.** One setting, **Line spacing**, in Settings › Appearance next to the font sizes, drives the line height of **both** the session output and the Focus view. It is a **multiplier of the font size**, from **1.0 to 1.8** (steps of 0.05), default **1.25**, stored in the WPF client state (`appearance` section, `lineSpacing`). The default is tighter than the earlier WPF look (1.45 on the transcript page; about 1.33 natural in the Focus cards) and close to the natural Segoe UI line (about 1.33).
- **Session output:** every row and markdown block of the transcript page (assistant, user, system, compaction, the live row) uses the one value through the CSS variable `--lh`, sent in the `appearance` message (`lh`). The small fixed-size labels (time, date) keep their own line height.
- **Focus view:** the card text (title, body, outcome of open, decided and closed cards) gets a fixed line height of font size × the value.
- **Live.** A change applies at once to both, without a restart; the virtualised transcript is measured again, as for a font size change.
- A value outside 1.0–1.8 or not a number is refused with a message and nothing is stored.

**FR.9.13 — Recency colour on the closed Recent dropdown (WPF).** The dropdown's button in the title bar (FR.6.7) shows the **open session's recency colour** (the four tiers of FR.9.10, the same colours as its row in the tree and the lists) behind the session name, so the age of the session you are working in is visible without opening the list. With no open session, or a session with no recent activity (tier 0), the button stays plain.
- **Setting.** Settings › Appearance has a checkbox **"Show recency colour in the closed Recent dropdown"**, **on by default**; stored in the client settings (`appearance` section, `showRecencyInRecentDropdown`). A file without the value reads as on.
- **Live.** Toggling it applies at once (no restart, no reopening); the tint also follows the open session and ages with the 60-second clock (FR.9.10). It concerns only the closed button: the popup's rows keep their own colours (FR.9.11).

**FR.9.14 — First paint of the stored theme.** The window is in the stored theme (Light or Dark, or the system's for System) **from its first paint**, not only after the theme has been switched back and forth: the frame, the backdrop behind the controls and the controls agree, in both themes. A stored Light theme on a system that is dark never shows a dark window, and the reverse. The window is styled for the stored theme when it is created, again once its first frame is on screen, and on every theme change, for every open window.

**FR.9.15 — The closed Recent dropdown keeps its recency colour on hover.** (Amends FR.9.13; extends FR.9.11.) Hovering the closed dropdown button, and opening the list, **do not replace** the button's background: the recency colour of the open session stays, and a **faint overlay** (the theme's hover highlight, partly transparent) is laid over it. The same holds for the rows of the open list (FR.9.11): hover shows only the overlay over the row's own recency colour. With no tint (tier 0 or the setting off) the overlay is all there is, as before.

**FR.9.16 — Active workers badge colours.** The Colours tab (FR.9.9) gets two entries in the group *Session status*: **Active workers badge: number** and **Active workers badge: outline**. Defaults, Light: number **`#E00000`** (red), outline **`#8B0000`** (dark red). Dark: number **`#FF5555`**, outline **`#B03030`** (lighter so they read on a dark background). Per theme, live, resettable, like every palette entry. The badge (FR.1.28) uses them as `Colour.ActiveWorkersText` and `Colour.ActiveWorkersBorder`.

**FR.9.17 — Limits-error warning colour.** The Colours tab (FR.9.9) gets the entry **Limits-error warning (triangle)** in the group *Session status*. Defaults: Light **`#E0A800`**, Dark **`#FFD33D`** (both yellow). Per theme, live, resettable. The warning of FR.1.29 uses it as `Colour.LimitWarning`.

**FR.9.18 — Colours tab layout and Apply.** (Amends FR.9.9.)
- **Apply.** An **Apply** button sits in the theme row, **all the way to the right** of the tab, on the same line as the Light/Dark switch and **Reset all <Theme> colours**. It commits a colour that is still being edited in an open picker (and closes the picker), then applies the edited theme's palette to every window and every open transcript page at once. Colours keep applying live as before (FR.9.9); Apply is the explicit "make sure it is applied everywhere now". When the edited theme is not the active one, Apply stores it; it shows when that theme becomes active.
- **Less space above the list.** Between the theme row and the first group heading there is at most **8 px** (the messages area takes no height while it is empty; the first heading has no extra top margin).
- **Two columns.** Within each group the colour rows are laid out in **two equal columns** (left column first, then right, row by row). The label trims with an ellipsis and shows in full as a tooltip; swatch and Reset keep their size. Under a width of about 640 px the list falls back to one column.
- **Gap before the scrollbar.** The rightmost swatch or Reset button ends at least **8 px** before the list's vertical scrollbar (and before the tab's right edge when no scrollbar is shown).

**FR.9.19 — Colour picker: pending colours, named colours, harmonies.** (Amends FR.9.9 *Live* and FR.9.18 *Apply*.)
- **Pending, not live.** Dragging in the picker (hue, saturation and brightness area, opacity slider) changes only the picker's **new colour** preview; nothing is stored or applied while the pointer moves. Closing the picker (click outside, or Enter) keeps the colour as **pending** on its row: the row's swatch shows it, with a small dot marking it as not applied yet. **Esc** in the picker drops the edit and keeps the previous colour.
- **Apply.** **Apply** stores every pending colour of the edited theme and applies them everywhere at once: the application brushes and every open transcript page (FR.9.18). The window's **Save** does the same. **Close** with pending colours asks whether to apply them, as for any other unsaved setting.
- **Reset and Reset all** are pending too, until Apply or Save.
- **Applied means visible.** After Apply the colour shows in the tree, the lists, the session output and the Focus view without a restart or a theme switch.
- **Named colours.** The picker has a **Named colours** section listing the 140 standard CSS / HTML named colours (`aliceblue` … `yellowgreen`, `palegoldenrod` included), each as a small swatch with its name, with a **filter box** that matches part of the name. Clicking one makes it the new colour (opacity kept). When the new colour equals a named colour, its name is shown next to the preview.
- **Harmonies.** For the new colour the picker shows three rows of clickable swatches: **Complementary** (hue + 180°), **Analogous** (hue − 30° and + 30°) and **Triadic** (hue + 120° and + 240°), keeping saturation, brightness and opacity. Clicking one makes it the new colour; the harmonies follow the new colour as it changes.
- **Size.** The picker stays a popup next to the swatch; it may grow, but it fits on the screen (it opens upward or to the side when there is no room below).
