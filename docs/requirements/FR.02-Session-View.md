> Area file of the requirements (index: [README](README.md); old IDs and where they went: [ID-MAP](ID-MAP.md)). The text below is the unchanged existing text of this area; the batch of 2026-11-17 is at the end.

### FR.2 — Session View (Right Panel)

**FR.2.1** Selecting a Session node in the tree opens its session view in the right panel.

**FR.2.2** The session view shall display the session's user-assigned name (from `appsettings.json`) at the top as a header.

**FR.2.3** The session view shall render the conversation in a terminal-like format, visually distinguishing:
- User input (prompts sent)
- Claude Code text output
- Tool use / tool result blocks
- System messages and status lines

**FR.2.3.1 — Message timestamps:** Each message bubble displays a timestamp. User messages show both date (`yyyy-MM-dd`) and time (`HH:mm`). Assistant messages show time only (`HH:mm`) — the date is omitted to reduce visual clutter.

**FR.2.3.4 — User bubble profile label:** The user message bubble header displays "You" on the left and the active profile display name (e.g. the account email, or "Default") on the right of the same row. The profile name is persisted in the session file and is restored when the session is loaded.

**FR.2.3.5 — Assistant model/effort label:** The first assistant message rendered after each user prompt displays a small italic label above the response content in the format `[<modelId>, <effort>]` (e.g. `[claude-sonnet-4-6, max]` or `[default, default]`). The label is persisted in the session file and restored on load. Subsequent assistant messages within the same turn (e.g. after a tool call) do not repeat the label. For scheduled/orchestrated turns (FR.14, FR.15), the model is recorded from the session's effective model and effort is written as `"default"` since effort is a user-facing command-bar option only.

**FR.3.3 — Session file format (extended):** The standard header line optionally carries space-delimited `key="value"` metadata after the role keyword:
- USER entries: `profile="<displayName>"`
- ASSISTANT entries: `model="<modelId>" effort="<effortLevel>"`

Old session files without metadata are parsed correctly (backward-compatible). The role keyword is always the first word after the timestamp; metadata keys never conflict with role names.

**FR.3.3 amendment — parse rule (amended 2026-11-12, FR.25):** Only a real header line ends an entry. A real header is exactly what the entry header parser accepts: `[` + a UTC timestamp `yyyy-MM-ddTHH:mm:ssZ` + `]` + a space + a known role keyword (`USER`, `ASSISTANT`, `SYSTEM`, `COMPACTION`), optionally followed by `key="value"` metadata. Any other body line, including one that starts with `[` — `[Tool: Read] x`, `[Scheduled] …`, `[Interrupted by user]`, a markdown link such as `[text](url)` — belongs to the entry body. Before this amendment the splitter treated any body line starting with `[` as the start of a new entry, which cut such entries short: many SYSTEM and USER entries showed empty content and were hidden. The parser stays tolerant of old files; a real header embedded in a body still splits the entry, as before.

**FR.2.3.2 — Text selectability:** All rendered text in the output panel must be selectable (copy-able) by the user, including user prompts, assistant responses (both plain text and markdown mode), headings, list items, code blocks, and system messages. Text blocks display an I-beam cursor on hover and show a visible blue selection highlight when text is selected. Cross-block selection is supported: when a pointer drag crosses from one text block into another, the selection extends across all intervening blocks. The first and last blocks receive partial selection; intermediate blocks are fully selected. Ctrl+C copies the combined cross-block text. Auto-scrolling is triggered when the pointer nears the viewport edges during a drag. This works uniformly in both plain-text mode (across messages) and markdown mode (across paragraphs, headings, code blocks within and across messages). Cross-message selection works within the rendered window of the transcript (about 1.5 screens either side of the viewport; Architect decision A4, 2026-11-15).

**FR.2.3.3 — Markdown table rendering:** Tables in markdown responses shall render with:
- Theme-aware border and header colors that adapt correctly to both dark and light themes (taken from the platform theme's low-emphasis foreground and chrome colours)
- Column alignment (left, center, right) as specified in the markdown source via `:---`, `:---:`, `---:` syntax
- Alternating row shading on data rows for readability
- Header row visually distinguished with a background fill

**FR.2.4** The session view shall provide a multi-line input text box at the bottom for composing and submitting prompts to Claude Code. Submission is via `Ctrl+Enter` or a dedicated Send button.

**FR.2.5** When Claude Code is running (processing a prompt), the input area shall be disabled and a visual busy indicator shown.

---

---

## Amendments and additions (batch 2026-11-17)

> Rows are described for the **WPF transcript** (a WebView2 page).

New IDs start at **FR.2.6**.

### Amendments to existing requirements

| Existing | Amended by | Change |
|---|---|---|
| FR.2.3.1 Message timestamps | FR.2.9, FR.2.7 | The user row shows the date and the time on **two lines at the left**; assistant and system rows show the time on the right. The date is not repeated in assistant rows. |
| FR.2.3.4 User bubble profile label | FR.2.9 | The word "You"/"USER" is gone from the row header. The profile name stays on the right of the header row. |
| FR.2.3.5 Assistant model/effort label | FR.2.7 | Same content; it is now a small italic line above the response, not part of a header with a role word. |
| FR.2.3.2 Text selectability | FR.2.10 | Restated for every row type, and for Focus cards (FR.23.20). |

### New requirements

**FR.2.6 — Input sources and input navigation.**
1. *Inputs* are the USER entries of the session view. *Mine* are typed by the user (desktop, web, Telegram). *Automated* are scheduled turns, mailbox deliveries and supervisor (orchestrated) prompts.
2. Automated inputs render **teal** (readable in light and dark themes) and carry the small label **SCHEDULED** or **SUPERVISOR**; this label replaces nothing, because a user row has no role word (FR.2.9).
3. The source is stored as an additive `source` meta field on the USER entry header (omitted for the desktop user). Older builds ignore it. A legacy USER entry beginning with `[Scheduled]` counts as scheduled, **when read only**; the legacy prefix is never written back as `source`.
4. In the WPF client a **click on an input selects it** (thin dotted border). Links, chips, buttons and "Show full entry" keep their meaning, a text-selecting drag does not change the selection, and a click in the row margin selects nothing. A click elsewhere in the transcript, or Esc, clears it.
5. **Up/Down** with an automated input selected moves to the previous/next input of any kind. With one of mine selected, only my inputs. The target is scrolled into view and selected, including inputs outside the loaded page or the virtualised window (the client asks for the older or newer page, and drops the pending move on an empty page).
6. With nothing selected and the transcript focused, Up/Down scroll by a line step and PgUp/PgDn by a page.
7. The selection is kept per session across re-renders, appends, live updates, paging and a reload of the same session; another session clears it. Find (Ctrl+F, F3), reference jumps and composer keys are unaffected.
8. Tessyn Web shows the colour and the label only.
Wire: `SessionEntryDto.source` (`tdcp-spec` additions §A1).

**FR.2.7 — Assistant rows.** No border, no background: the text sits on the view background. No role word. The time (`HH:mm:ss`) is in a fixed 48 px column at the **right**, bottom-aligned, small and faint. Model, effort and profile (FR.2.3.5) are one small italic line above the text.

**FR.2.8 — System rows.** System rows (tool calls, status lines) use the assistant layout (FR.2.7): no role word, the time in the right column. Their background is the palette key **`SystemRow`** (FR.9.9), default **pale blue**: light `#BFE0F7`, dark `#1A3A50`, editable in the Colours tab per theme.

**FR.2.9 — User rows.** A user row has **no role word** (no "USER", no "You"). The date (`yyyy-MM-dd`) and the time (`HH:mm`) are at the **left, on two lines**, the date above the time. The profile name (FR.2.3.4) is on the right of the row. The bubble colours are the palette keys of FR.9.2; automated inputs are teal (FR.2.6).

**FR.2.10 — Selectable text.** Every piece of rendered text in the transcript is selectable and copyable (FR.2.3.2), in every row type, in plain and markdown modes. A click that only ends a text selection never triggers the row's own click action (select an input, FR.2.6; open a link).

**FR.2.11 — Scrollbar.** The transcript has one custom scrollbar whose thumb position is **proportional to the entry index** (the top entry plus the fraction of it scrolled past, over the same figure at the end of the scroll range), because entries below the window have only estimated heights. A drag keeps the thumb exactly under the mouse, re-aims after each render and rebases when a page of older or newer entries arrives mid-drag, so the content does not jump. The thumb is exactly at the ends at the ends of the range. A track click pages toward the click; the wheel over the bar scrolls. Native scroll keys and wheel work as before.
