# UC.SH.01 — Window Chrome and Composer Layout

Related requirements: FR.6.5–FR.6.8, FR.12.30–FR.12.32, NR.7 (see `docs/requirements/`). Test cases: [TC.SH.01](/docs/testcases/TC.SH.01-Window-Chrome.md).

## Actors
- **User**. **Tessyn** (WPF client).

---

## UC.SH.01 — Recognise the app
**Main flow:** the User sees the Tessyn Web icon on the window, the taskbar button, the tray icon of the background host and the exe.
**Covers:** FR.6.5.

## UC.SH.02 — Drag the window by the version text
**Main flow:** the User presses on the version text and drags: the window moves; the text takes no focus and shows no focus rectangle; Tab skips it.
**Covers:** FR.6.6.

## UC.SH.03 — Title bar and menu
**Main flow:** 1. The title bar shows chevron, hamburger, day/night, the instruction icons, the Recent dropdown (labelled with the open session) and the window controls. 2. The hamburger lists Import, Settings, a separator and Exit. 3. The status bar meters fill the bar's height; Profiles is at the far right.
**Covers:** FR.6.7, FR.6.8.

## UC.SH.04 — Compose with a long text
**Main flow:** 1. The User types a very long text. 2. The input box takes the free height; Send (the only accent-coloured control) stays in view. 3. Streaming and Sound are icons on the selector line with a gap after Profile; the toggles use the regular button colour.
**Alternative flows:** **A1** — *Send later* in the header with a model picked: the schedule keeps the pick (FR.14.13). **A2** — New Branch is on: *Send later* is refused with a notice.
**Covers:** FR.12.30–FR.12.32, NR.7.
