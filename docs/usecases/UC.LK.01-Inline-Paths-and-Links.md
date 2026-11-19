# UC.LK.01 — Inline Paths and Links

Related requirements: FR.26.5, FR.26.6, FR.26.7, FR.26.8 (see `docs/requirements/`). Test cases: [TC.LK.01](/docs/testcases/TC.LK.01-Inline-Paths-and-Links.md).

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

## UC.LK.06 — See and play a video the session mentions
**Main flow:** the entry mentions a video path (mp4, mov, webm, mkv, avi, m4v, wmv); right under the path the first frame of the video appears, fitted to the column, with a small play badge. The User Ctrl+clicks the path or the frame and the video plays in the default media player. A plain click does nothing, so the User can select and copy the frame or the path.
**Alternative flows:** **A1** — The policy refuses the file: the path stays with "Video not allowed". **A2** — No frame can be made (damaged file, no codec, too large for a preview, shell timeout): "Preview unavailable"; Ctrl+click still opens the file. **A3** — The frame appears late: the entry is measured again and the view does not jump. **A4** — The daemon refuses on Ctrl+click: a dismissible notice, no dialog, no player.
**Covers:** FR.26.5, FR.26.6.

## UC.LK.07 — Open a file the session names by a relative path
**Main flow:** the entry names a file relative to the session's working directory (`docs/features/preview-windows.md`, `.\src\a.cs`, or `` `README.md` `` in backticks); it has the dotted underline like an absolute path. The User Ctrl+clicks it and it opens (preview pane, or the group's preview window). A relative image or video path shows its inline preview.
**Alternative flows:** **A1** — The path leaves the working directory (`..\..\outside.txt`): the daemon refuses, a dismissible message, no dialog. **A2** — The file does not exist: "File not found" (an image: "Image unavailable"). **A3** — A web address or a link target (`example.com/page.html`, `[t](a/b.md)`) and version-like text are not marked.
**Covers:** FR.26.7.

## UC.LK.08 — Read an HTML file the session names as a page
**Main flow:** the entry names an `.html` or `.htm` file; the User Ctrl+clicks the path and the page opens **rendered**, as authored (own colours, headings, tables, inline images), in the default Preview panel, a docked group panel or a preview window. The User selects and copies text from the page, zooms with Ctrl+wheel and clicks **Source**: the HTML shows as text (monospace); Source again returns to the page.
**Alternative flows:** **A1** — The page has a `<script>`, a form, a frame, or a remote image or stylesheet: nothing runs or loads; the rest of the page shows (the policy is in FR.26.8, Static). **A2** — The User clicks an http(s) link in the page: it opens as a link in a markdown preview does (matching group's window or panel, otherwise the system browser); a `#anchor` link scrolls within the page; any other link does nothing and the page stays. **A3** — The file is over 2 MB: the first 2 MB show with the usual note. **A4** — The page relies on files next to it (relative css or images): they show as missing; **Open in default app** opens the full page. **A5** — An svg file: still shown as text, as before.
**Covers:** FR.26.8.
