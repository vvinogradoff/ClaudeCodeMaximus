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
