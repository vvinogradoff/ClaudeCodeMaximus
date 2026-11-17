# UC.LK.01 — Inline Paths and Links

Related requirements: FR.26.5, FR.26.6 (see `docs/requirements/`). Test cases: [TC.LK.01](/docs/testcases/TC.LK.01-Inline-Paths-and-Links.md).

## Actors
- **User**. **Tessyn** (WPF transcript).

## Preconditions (common)
- An assistant entry that mentions local files (text, markdown, an image) and a web link.

---

## UC.LK.01 — See where a file is mentioned
**Main flow:** the User reads the entry; every local path is in the sentence where the model wrote it, with a dotted underline. No chip row appears under the message.
**Covers:** FR.26.5.

## UC.LK.02 — Open a file with Ctrl+click
**Main flow:** the User Ctrl+clicks a path (`.md`, `.csv`, `.txt`, code, any previewable file); the preview pane opens it.
**Alternative flows:** **A1** — The policy refuses the file: a dismissible message in the pane, no dialog. **A2** — A plain click does nothing.
**Covers:** FR.26.5, FR.26.6.

## UC.LK.03 — Select and copy without opening
**Main flow:** the User drags over a path, a link or an image to select it and copies it; nothing opens.
**Covers:** FR.26.6.

## UC.LK.04 — See which image the session means
**Main flow:** the entry mentions an image path; right under the path the image appears, fitted to the column. The User Ctrl+clicks the path or the image to open it in the preview pane, or selects the image and copies it.
**Alternative flows:** **A1** — The policy refuses the file: the path stays with "Image not allowed". **A2** — The file is missing: "Image unavailable". **A3** — The image loads late: the entry is measured again and the view does not jump.
**Covers:** FR.26.5, FR.26.6.

## UC.LK.05 — Open a web link
**Main flow:** the User Ctrl+clicks a link; it opens (http and https only). A plain click does nothing.
**Covers:** FR.26.3, FR.26.6.
