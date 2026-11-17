# UC.CL.01 — Colours Settings

Related requirements: FR.9.9, FR.9.10, FR.2.8 (see `docs/requirements/`). Test cases: [TC.CL.01](/docs/testcases/TC.CL.01-Colours-Settings.md).

## Actors
- **User**. **Tessyn** (WPF client).

## Preconditions (common)
- Settings is open (the daemon is connected).

---

## UC.CL.01 — Edit a colour with the picker
**Main flow:** 1. The User opens the **Colours** tab. 2. Rows show a name and a swatch, no hex text. 3. Clicking a swatch opens a colour picker. 4. The User picks a colour; the tree rows (or the transcript) change at once.
**Alternative flows:** **A1** — *Reset* on a row returns that colour to its default. **A2** — *Reset all* returns every colour of the theme shown.
**Covers:** FR.9.9.

## UC.CL.02 — Light and Dark are separate
**Main flow:** 1. The tab starts on the active theme (Dark). 2. The User changes the system-row colour. 3. The User switches the tab to Light: the Light value is still the Light default. 4. The User changes the app theme to Light: the transcript uses the Light value. 5. After a restart both values are kept.
**Covers:** FR.9.9, FR.2.8.

## UC.CL.03 — Every themed colour is there
**Main flow:** the User scrolls the tab and finds the groups Session output, Session lists, Focus and Window; each key has a Light and a Dark default of the catalogue (FR.9.9).
**Covers:** FR.9.9, FR.9.10.
