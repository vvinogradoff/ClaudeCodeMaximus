# UC.FC.01 — Focus: Closed Items, Selectable Text, Send Answers

Related requirements: FR.23.20–FR.23.23 (see `docs/requirements/`), FR.23.14, FR.23.15. Related: [UC.FV.01](/docs/usecases/UC.FV.01-Focus-View.md), [UC.FD.01](/docs/usecases/UC.FD.01-Focus-Reply-Drafts.md). Test cases: [TC.FC.01](/docs/testcases/TC.FC.01-Focus-Closed-and-Send-Answers.md).

## Actors
- **User**. **Tessyn** (WPF and Avalonia clients).

## Preconditions (common)
- A session with open, Decided and closed Attention Items.

---

## UC.FC.01 — Review what was closed
**Main flow:** 1. The User selects Focus. 2. Open cards show; *Decisions (n)* and *Closed (n)* are collapsed. 3. The User expands Closed: compact cards, newest first, with `Answered: …`. 4. Clicking a header row shows the body.
**Alternative flows:** **A1** — Nothing open: the empty state shows and the expanders still show. **A2** — Switching session, or selecting Focus again, collapses both.
**Covers:** FR.23.23.

## UC.FC.02 — Copy text from any card
**Main flow:** the User selects and copies the title, body or outcome line of an open, Decided or closed card; a drag inside a closed card's body does not collapse it.
**Covers:** FR.23.20.

## UC.FC.03 — Send answers from the header
**Main flow:** 1. In Focus the header shows **Send answers (0)**, disabled. 2. The User types in two reply boxes: **(2)**, enabled. 3. Clicking it, or pressing **Ctrl+Enter** in a box, sends one prompt with both answers; items close as Answered; the main input and its draft are untouched.
**Alternative flows:** **A1** — Ctrl+Enter with all boxes empty: nothing happens, no newline. **A2** — Plain Enter, Shift+Enter: newline; Ctrl+Shift+Enter: no send. **A3** — In Session the button is hidden. **A4** — While a turn runs the send is allowed.
**Covers:** FR.23.21, FR.23.22, FR.23.15.
