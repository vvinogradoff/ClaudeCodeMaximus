# FR.2 — Session View, transcript rows (batch 2026-11-17)

> **Staging file, phase 1.** New and amended text only; phase 2 puts the existing FR.2 text (and FR.3, which stays its own file) around it. Use cases: [UC.TR.01](/docs/usecases/UC.TR.01-Transcript-Rows.md), [UC.IN.01](/docs/usecases/UC.IN.01-Input-Navigation.md). Test cases: [TC.TR.01](/docs/testcases/TC.TR.01-Transcript-Rows.md), [TC.IN.01](/docs/testcases/TC.IN.01-Input-Navigation.md).
> Rows are described for the **WPF transcript** (a WebView2 page); Avalonia already looks like this and is the visual reference.

New IDs start at **FR.2.6**.

## Amendments to existing requirements

| Existing | Amended by | Change |
|---|---|---|
| FR.2.3.1 Message timestamps | FR.2.9, FR.2.7 | The user row shows the date and the time on **two lines at the left**; assistant and system rows show the time on the right. The date is not repeated in assistant rows. |
| FR.2.3.4 User bubble profile label | FR.2.9 | The word "You"/"USER" is gone from the row header. The profile name stays on the right of the header row. |
| FR.2.3.5 Assistant model/effort label | FR.2.7 | Same content; it is now a small italic line above the response, not part of a header with a role word. |
| FR.2.3.2 Text selectability | FR.2.10 | Restated for every row type, and for Focus cards (FR.23.20). |

## New requirements

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

**FR.2.8 — System rows.** System rows (tool calls, status lines) use the assistant layout (FR.2.7): no role word, the time in the right column. Their background is the palette key **`SystemRow`** (FR.9.9), default **pale blue**: light `#BFE0F7`, dark `#1A3A50` (the Avalonia values), editable in the Colours tab per theme.

**FR.2.9 — User rows.** A user row has **no role word** (no "USER", no "You"). The date (`yyyy-MM-dd`) and the time (`HH:mm`) are at the **left, on two lines**, the date above the time, as in Avalonia. The profile name (FR.2.3.4) is on the right of the row. The bubble colours are the palette keys of FR.9.2; automated inputs are teal (FR.2.6).

**FR.2.10 — Selectable text.** Every piece of rendered text in the transcript is selectable and copyable (FR.2.3.2), in every row type, in plain and markdown modes. A click that only ends a text selection never triggers the row's own click action (select an input, FR.2.6; open a link).

**FR.2.11 — Scrollbar.** The transcript has one custom scrollbar whose thumb position is **proportional to the entry index** (the top entry plus the fraction of it scrolled past, over the same figure at the end of the scroll range), because entries below the window have only estimated heights. A drag keeps the thumb exactly under the mouse, re-aims after each render and rebases when a page of older or newer entries arrives mid-drag, so the content does not jump. The thumb is exactly at the ends at the ends of the range. A track click pages toward the click; the wheel over the bar scrolls. Native scroll keys and wheel work as before.
