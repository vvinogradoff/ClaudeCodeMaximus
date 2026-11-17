# FR.9 — Theme and Colour Customisation (batch 2026-11-17)

> **Staging file, phase 1.** New and amended text only. Use cases: [UC.CL.01](/docs/usecases/UC.CL.01-Colours-Settings.md), [UC.LP.01](/docs/usecases/UC.LP.01-Left-Panel-Lists.md). Test cases: [TC.CL.01](/docs/testcases/TC.CL.01-Colours-Settings.md). New IDs start at **FR.9.9**.

## Amendments to existing requirements

| Existing | Amended by | Change |
|---|---|---|
| FR.9.2 Per-theme custom colours | FR.9.9 | The list of editable colours grows from 12 to the full palette; **every** colour has a Light and a Dark value. |
| FR.9.3 Colour application | FR.9.9 | The sentence "the editable colour palette is dropped at WPF parity" is **withdrawn**: the Colours tab is that editor (it replaces decision A8). |
| FR.9.4 Sensible defaults | FR.9.9 | Defaults are the Avalonia colours, separately for Light and Dark. |
| **FR.9.5 Session recency bars** | **FR.9.10** | **Replaced.** Three tiers (15/30/60 min, last prompt) become four tiers on the latest activity, plus the dotted border. FR.9.5 stays as an ID and points to FR.9.10. |
| FR.9.6 Recent-dropdown hide | FR.9.11 | A row keeps its recency colour in every state of the dropdown. |
| FR.9.8 Pinned sessions and order | FR.1.20 | Row content and layout in the dropdown. |

## New requirements

**FR.9.9 — Colours tab (WPF).**
- Settings has a **Colours** tab (section `colours` in the client state).
- **Theme.** The tab edits one theme at a time, **Light** or **Dark**, chosen by a switch at its top (it starts on the active theme). Every colour has **its own value per theme**; changing one theme never changes the other. Values are saved **per theme**.
- **Scope.** The palette covers every themed colour of the client, in groups: *Session output* (input box background and text, user bubble, code block, inline code, **system rows** `SystemRow`, automated input teal), *Session lists* (the four recency tiers, the 24-hour border, running and resumable squares, the selected row), *Focus* (kinds Critical, Question, Todo, Alert, Decided: background and text each) and *Window* (accent, button, toggle-on states, title bar checked state).
- **Colour picker.** Each row shows a **swatch** that opens a **colour picker** (hue, saturation and brightness area, an opacity slider, previews of the old and the new colour). **The hex text is not shown** in the row and not typed by the user; the picker is the only way to edit. A **Reset** per row returns that colour to the theme's default, and **Reset all** returns every colour of the theme shown.
- **Storage.** A value equal to the theme's default is not stored. A theme never inherits from the other theme.
- **Defaults** are the Avalonia colours: Green, LightGreen, HoneyDew, MintCream, DarkGreen, DarkRed, DodgerBlue, `#8B0000`, `#DAA520`, `#707070` and the pale blue `#CDE6F7` (dark `#264F78`) for the selected row; Todo and Alert follow the theme; each has its light and dark default.
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
