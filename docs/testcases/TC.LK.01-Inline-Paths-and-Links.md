# TC.LK.01 — Inline Paths and Links

Related requirements: FR.26.5, FR.26.6. Use cases: [UC.LK.01](/docs/usecases/UC.LK.01-Inline-Paths-and-Links.md).

**Legend.** AUTOMATED = unit tests, renderer tests and node script tests (`InlinePathMarkerTests`, `PathSlotsScriptTests`, `SessionPaneNavigationTests`, `InlineVideoTests`, `ShellVideoThumbnailServiceTests`); E2E = WPF end-to-end over CDP (NR.6, classes `WpfInlinePathsE2ETests` and `WpfInlineVideoE2ETests`, and `WpfFocusE2ETests` for the preview of a path).

| TC | Title | Kind | UC | FR |
|---|---|---|---|---|
| TC.LK.01 | A bare path, a path in backticks, a path in a code block and a UNC path are marked in place; text around them is unchanged; no chip row | AUTOMATED, E2E | UC.LK.01 | FR.26.5 |
| TC.LK.02 | Paths inside links, in user entries and in entries without paths are not marked; the output adds no element that loads a resource | AUTOMATED | UC.LK.01 | FR.26.5 |
| TC.LK.03 | Ctrl+left-click on a markdown and on a text path opens the preview pane through `file.preview` | E2E | UC.LK.02 | FR.26.5, FR.26.6 |
| TC.LK.04 | A plain click on a path opens nothing | E2E | UC.LK.02 | FR.26.6 |
| TC.LK.05 | Only Ctrl + left button opens: Shift or Alt with it, the middle and right buttons and non-click events do not | AUTOMATED | UC.LK.02 | FR.26.6 |
| TC.LK.06 | A drag over a path selects its text and opens nothing | E2E | UC.LK.03 | FR.26.6 |
| TC.LK.07 | The pointer cursor on a path, a link and an image only while Ctrl is held; the path has a dotted underline | E2E | UC.LK.02 | FR.26.5, FR.26.6 |
| TC.LK.08 | An image path shows the image directly under it, fitted to the column (max 360 px high), from the preview cache only | AUTOMATED, E2E | UC.LK.04 | FR.26.5 |
| TC.LK.09 | The first occurrence of an image path gets the slot; svg and non-image files get none | AUTOMATED | UC.LK.04 | FR.26.5 |
| TC.LK.10 | An image source outside the preview-cache host never becomes an `<img>` | AUTOMATED | UC.LK.04 | FR.26.5 |
| TC.LK.11 | Dragging from the path down past the image selects the image; a plain click on it opens nothing; Ctrl+click on it opens the preview | E2E | UC.LK.03, UC.LK.04 | FR.26.5, FR.26.6 |
| TC.LK.12 | A file the access rules refuse shows "Image not allowed", a missing one "Image unavailable"; neither reads the file | AUTOMATED, E2E | UC.LK.04 | FR.26.5 |
| TC.LK.13 | The inline image asks the daemon like a click (`file.preview`), is copied to the preview cache once per entry and path, and does not open the preview pane | AUTOMATED | UC.LK.04 | FR.26.5 |
| TC.LK.14 | A path that is not path-shaped never reaches the daemon | AUTOMATED | UC.LK.04 | FR.26.5 |
| TC.LK.15 | After the image loaded the entry height is the measured one (re-measure) | E2E | UC.LK.04 | FR.26.5 |
| TC.LK.16 | A web link: plain click posts nothing, Ctrl+click posts it to the host; the host keeps the http/https policy | E2E | UC.LK.05 | FR.26.3, FR.26.6 |
| TC.LK.17 | The page's CSP names only the app's own virtual hosts (the media proxy and the preview cache for images) | AUTOMATED | UC.LK.04 | FR.26.5 |
| TC.LK.18 | A video path (mp4, mov, webm, mkv, avi, m4v, wmv) gets the slot for its first occurrence only; ogv, mp3, svg and a name without extension get none | AUTOMATED | UC.LK.06 | FR.26.6 |
| TC.LK.19 | The shell makes a PNG first frame of a real H.264 clip (landscape, right size); a file that is not a video, a missing file and a cancelled call give none, without throwing | AUTOMATED (real shell, `ShellVideoThumbnailServiceTests`) | UC.LK.06 | FR.26.6 |
| TC.LK.20 | A loaded frame shows under the path with the play badge; a frame source outside the preview-cache host never becomes an `<img>`; the hints are "Video not allowed", "Preview unavailable" and "Loading preview" | AUTOMATED | UC.LK.06 | FR.26.6 |
| TC.LK.21 | A video path asks the daemon like a click (`file.preview`), the shell reads the video from the preview cache, the video is deleted afterwards and only the frame stays; answered once per entry and path; the pane stays closed | AUTOMATED | UC.LK.06 | FR.26.6 |
| TC.LK.22 | A refusal answers "not allowed" and reads nothing; no frame answers "unavailable" and leaves nothing in the cache; a video over 150 MB is not fetched | AUTOMATED | UC.LK.06 | FR.26.6 |
| TC.LK.23 | Ctrl+click on a video path opens a copy from the preview cache through the allowlisted launcher, not the pane; a second Ctrl+click does not fetch again; a daemon refusal gives a notice and starts nothing; non-video paths still open the pane | AUTOMATED | UC.LK.06 | FR.26.6 |
| TC.LK.24 | E2E over CDP with a real 3 KB clip: the first frame (made by the shell in the test user's session) appears directly under the path, centred play badge, fitted to the column, entry re-measured; a plain click on the path or the frame asks for nothing; the pointer cursor only with Ctrl; Ctrl+click on the frame and on the path asks the host to open it in the default player (a test window only logs the request) and the pane stays closed | E2E | UC.LK.06 | FR.26.6 |
| TC.LK.25 | E2E: a file named .mp4 that is not a video shows "Preview unavailable", a clip under a credentials folder "Video not allowed", and only the real clip became a frame | E2E | UC.LK.06 | FR.26.6 |
| TC.LK.26 | Marking, positive: `docs/features/preview-windows.md`, `code\App.xaml.cs`, `./src/a.ts`, `..\x\y.cs`, `.github/workflows/ci.yml` and a spaced path inside backticks or quotes are marked in place (dotted underline); `IsPathShaped` accepts exactly these shapes | AUTOMATED | UC.LK.07 | FR.26.7 |
| TC.LK.27 | Marking, negative: `https://x/a.md`, `[t](a/b.md)` (link behaviour kept), `example.com/page.html` (top-level-domain list), `1.2.3`, `v2.0`, a path without extension, an absolute path (not marked twice) and paths in user entries are not marked as relative paths | AUTOMATED | UC.LK.07 | FR.26.7 |
| TC.LK.28 | A bare file name with an extension (`README.md` in backticks) is marked only inside backticks; outside them, or with a capitalised extension (`string.Empty`), it is not | AUTOMATED | UC.LK.07 | FR.26.7 |
| TC.LK.29 | Daemon: `file.preview` with a relative path resolves it against the working directory (`docs/plan.md`); `..\..\outside.txt` is refused with "the relative path leaves the working directory" | AUTOMATED | UC.LK.07 | FR.26.7 |
| TC.LK.30 | E2E: Ctrl+click on a relative `.md` path asks `file.preview` with the relative text as written and opens the preview; a plain click asks for nothing | E2E | UC.LK.07 | FR.26.7 |
| TC.LK.31 | E2E: a relative image path gets the inline image preview directly under it; Ctrl+click on it opens the file | E2E | UC.LK.07 | FR.26.7 |
| TC.LK.32 | Prose with apostrophes ("It's fine; see docs/a.md. Don't worry.", "The user's file is src/main.cs and the dev's note") still marks the paths; only a quoted range that is itself a path suppresses bare hits | AUTOMATED | UC.LK.07 | FR.26.7 |
| TC.LK.33 | Bare names in backticks need a known file-type extension: `Program.cs` is marked, `obj.method`, `os.path`, `item.name` are not | AUTOMATED | UC.LK.07 | FR.26.7 |
| TC.LK.34 | A path starting with `~`, one whose first part is `localhost`, and a markdown link target `](docs/a.md)` are not marked and take no path slot | AUTOMATED | UC.LK.07 | FR.26.7 |
| TC.LK.35 | A relative path inside a code block is marked exactly as an absolute one is | AUTOMATED | UC.LK.07 | FR.26.7 |
| TC.LK.36 | A `.html` file opens rendered by default (heading and styled text appear as page content, not as markup text); Source is offered | AUTOMATED (view model, surface fake), E2E | UC.LK.08 | FR.26.8 |
| TC.LK.37 | Source shows the HTML as monospace text (the text kind); Source again shows the page; the choice is per open item and a newly opened file starts rendered, as markdown does | AUTOMATED, E2E | UC.LK.08 | FR.26.8 |
| TC.LK.38 | A page with `<script>` shows its content and the script does not run (a script that would change the text leaves it unchanged) | E2E | UC.LK.08 | FR.26.8 |
| TC.LK.39 | A remote image (`https://...`) and a remote stylesheet are not requested (no network call seen) and show as missing | E2E | UC.LK.08 | FR.26.8 |
| TC.LK.40 | An inline `<style>`, a `style` attribute and an embedded `data:` image show | E2E | UC.LK.08 | FR.26.8 |
| TC.LK.41 | Forms, frames and objects in the page do not render or submit; no navigation happens | E2E | UC.LK.08 | FR.26.8 |
| TC.LK.42 | Click on an http(s) link in the page goes through the preview routing (FR.29.4: matching group or system browser) and the preview stays on the page | AUTOMATED (router fake), E2E | UC.LK.08 | FR.26.8 |
| TC.LK.43 | A `#anchor` link scrolls within the page and the page is not replaced | E2E | UC.LK.08 | FR.26.8 |
| TC.LK.44 | Relative, `file:`, `javascript:` and `mailto:` links do nothing; the pane never navigates away from the page | AUTOMATED (policy), E2E | UC.LK.08 | FR.26.8 |
| TC.LK.45 | A `.htm` file and a file served as `text/html` without a known extension both render | AUTOMATED, E2E (.htm) | UC.LK.08 | FR.26.8 |
| TC.LK.46 | An `.svg` file is still shown as text, with no Source toggle | AUTOMATED, E2E | UC.LK.08 | FR.26.8, FR.26.5 |
| TC.LK.47 | A file over 2 MB shows its first 2 MB (rendered) with the truncation note | AUTOMATED | UC.LK.08 | FR.26.8 |
| TC.LK.48 | The default Preview panel, a docked group panel and a preview window each render the page and show the Source button (title-bar icon in the window) | E2E | UC.LK.08 | FR.26.8, FR.29.14, FR.29.18 |
| TC.LK.49 | Switching Tessyn's light/dark theme does not recolour the page (a page without a background stays white); the pane around it follows the theme | MANUAL (look; E2E not built) | UC.LK.08 | FR.26.8 |
| TC.LK.50 | Access checks as for other files: `file.preview` refuses a path not in the entry and a FileAccess-denied path; a refusal shows the dismissible message | AUTOMATED | UC.LK.08 | FR.26.8, FR.26.5 |
| TC.LK.51 | Relative css or images next to the file are not loaded; **Open in default app** is offered for the page | E2E (not built, see report) | UC.LK.08 | FR.26.8 |
| TC.LK.52 | Text selection and copy work on the rendered page; Ctrl+wheel zooms it as the pane does today | MANUAL (the page has no Ctrl+wheel zoom today) | UC.LK.08 | FR.26.8 |
| TC.LK.53 | The page is isolated from the preview page itself: the HTML cannot reach the pane's own script or host messages | AUTOMATED (policy/markup), E2E | UC.LK.08 | FR.26.8 |

## FR → TC coverage
| FR | Test cases |
|---|---|
| FR.26.5 | TC.LK.01–02, 03, 07–15, 17 |
| FR.26.6 | TC.LK.03–07, 11, 16, 18–25, 30 |
| FR.26.7 | TC.LK.26–35 |
| FR.26.8 | TC.LK.36–53 |
