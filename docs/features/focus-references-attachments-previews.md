# Focus references, attachments and output previews — design

> Draft for Architect review, 2026-11-15. Author: Developer/Claude. Requested by the Architect on 2026-11-15.
> Open decisions are pinned in the supervisor session's Focus view:
> - A9: which desktop client gets the UI;
> - A11: what the web may read;
> - A12: attachment storage;
> - A13: remote media auto-load.
>
> FR numbers are provisional. FR.23 is the Focus view, FR.25 is Tessyn Web, FR.26 is link fidelity (plan Appendix A).

## 1. What the Architect asked for

1. **Focus references.**
   - A session's Focus item (ask, alert, …) can point at specific parts of the output of this or another session, and at files on disk.
   - The user can jump to a referenced part and come back.
   - The user can preview a referenced file, for example a plan written as .md that awaits approval.
   - All of this must work in the web client too.
2. **Attachments from the input area.**
   - The user can send files to a session, mostly images and video. This matters most on the web.
   - A file is stored in app data, and the session refers to it by path.
   - On desktop, drag-and-drop inserts the file's full path; a "file mode" toggle copies the file into app data first.
3. **Output previews.**
   - When a session prints an https link (e.g. a WaveSpeed result) or a local file path, the user can preview it inline.
   - This must work on the web too, which means reading files on the Z13 from the phone.
4. **A native WPF look** (handled separately; see §6).

## 2. Shared building blocks (daemon)

### 2.1 References on Focus items (FR.23 amendment)

`post_attention` items gain an optional `refs` array, with up to 10 per item. Each ref is one of:

| Kind | Fields given by the session | Resolved and stored by the daemon |
|---|---|---|
| `entry` | `session` (short id or node id; default = the caller), `quote` (exact text from the entry, at most 300 chars), `occurrence` (`last`, the default, or `first`), `label` | `nodeId`, `fileId`, `entryId`, `offset`/`length` of the quote inside the entry |
| `file` | `path` (absolute, or relative to the session's working directory), `label` | `refId` (opaque, 128-bit random), file name, size, MIME type, mtime |
| `url` | `url` (http/https only), `label` | as given |

- **Entry refs.** Resolution happens when the item is posted, against the content store (P1.E/F ids). Whitespace is normalised for matching, and the quote must match exactly once in the chosen direction. A ref that cannot be resolved makes the whole call fail with a message naming it, so the model can fix its quote and retry.
- **File refs.** The file must exist and be a regular file.
- **Storage.** Refs are stored with the item in the attention ledger. They survive restarts, because entry ids are persisted per-file ordinals.

### 2.2 File handles and reading

- Clients never send a raw path to read a file. They send a handle:
  - `refId`, for a file reference;
  - `attachmentId`, for an attachment (§3);
  - or `(nodeId, entryId, path)` for a path that appears in that entry's text (§4).

  The daemon checks that the handle exists and that the file is still the same one (size and mtime may change; it must be the same path).
- **New method** `file.read { handle, offset?, length? }` returns `{ name, mime, size, mtime, offset, data }`. `data` is UTF-8 text for text types and base64 for binary, chunked up to a per-call cap.
- **Policy** (`AppSettings.FileAccess`):
  - type allowlist: md, txt, code, json, images, video, pdf;
  - denied paths: `.ssh`, token stores, `%AppData%\TessynDesktop\*.dat`, credentials files;
  - size caps;
  - every relay read is logged.
- **Relay transport** (decision A11): only handle-based reads, with the default *referenced files and attachments only*. Option 2 of A11 widens this to files under the session's working directory.
- **Loopback.** Desktop clients may read any file a handle points to. They can also open files locally.

### 2.3 Navigation to an entry

- `session.entries` gains an additive `aroundEntryId` parameter. It returns the page centred on that entry, so a client can open a session at the referenced entry and highlight `offset`/`length`.
- **Back navigation** is client state: a stack of `(session, scroll anchor)`. Every client gets a Back control.

### 2.4 Wire summary (all additive; the web contract is extended through an FR.25 amendment)

| Item | Loopback | Relay (web) |
|---|---|---|
| `attention.list` items gain `refs[]` (no absolute paths, only name and working-dir-relative path) | yes | yes |
| `session.entries { aroundEntryId }` | yes | yes |
| `file.read { handle, … }` | yes | yes, policy-gated (A11) |
| `attachment.*` (§3) | yes | yes |
| `file.preview { nodeId, entryId, path }` returns a handle (§4) | yes | yes, policy-gated |

## 3. Attachments (new FR, provisional FR.31)

- **Storage** (decision A12, per profile): `%AppData%\TessynDesktop\attachments\<profileKey>\<nodeId>\<yyyyMMdd-HHmmss>-<name>` (UTC). `profileKey` is the session's resolved profile id, `default` for the Default profile. Nothing is deleted automatically. Name collisions get `-2`, `-3`.
- **Desktop:**
  - Drag-and-drop onto the input inserts the full path(s) at the caret.
  - A "file mode" toggle beside the input instead copies the file into the session's attachments folder and inserts that path.
- **Web:**
  - An attach button in the input area. The browser uploads the file to the Tessyn.Web server (HTTP multipart, cap 200 MB per A12).
  - The server tells the daemon over the link (`attachment.fetch { blobId, name, size, sha256 }`).
  - The daemon downloads the blob from the server over HTTPS with its link token, checks the hash, stores the file and returns the path.
  - The server then deletes the blob, and the SPA inserts the path into the prompt.
- **Session side:** the inserted text is a plain path (e.g. `Attached file: C:\...\attachments\…\photo.png`). Sessions read it like any other file. No MCP change is needed.

## 4. Output previews (FR.26 extension)

- **Detection** in the transcript renderers:
  - https URLs whose path ends in an image or video extension, or whose host is on the media allowlist;
  - absolute local paths (`C:\…`, `\\server\…`) in session output.
- **https media** (decision A13, default):
  - auto-load from the allowlist (`annielartens.online`, `*.githubusercontent.com`, editable in Settings);
  - a "load preview" button for everything else.
- **Local files:** shown as a chip.
  - On desktop, a click previews the file: markdown rendered, images, text and code, video.
  - On the web, a click asks `file.preview { nodeId, entryId, path }`. The daemon checks that the path really occurs in that entry's text, applies the A11 policy and returns a handle for `file.read`.

## 5. Clients

- **Web (SPA):**
  - Focus cards list their refs. Tapping an entry ref opens the session at that entry with a Back button; tapping a file ref opens a preview sheet (markdown, text and code, image, video).
  - An attach button in the input.
  - Inline media previews and local-file chips in the transcript.
- **WPF:** the same features in the WPF Focus view and transcript; the WebView2 transcript makes previews cheap.
- **Avalonia:** nothing new. Decision A9: new desktop UI goes into WPF only, and Avalonia is being sunset.

## 6. Native WPF look (separate task)

- Drop the dense overrides (12 px font, tight paddings) and use the Fluent defaults.
- Use Segoe Fluent Icons instead of text glyphs.
- Give the flat virtualized tree the look of a Fluent `TreeView`: chevrons, hover and selection visuals.
- Use the system accent colour, and a Mica backdrop where available.

## 7. Phasing

| Step | Content | Depends on |
|---|---|---|
| 1 | Daemon: refs on Focus items, quote resolution, file handles, `file.read`, `aroundEntryId`, policy setting with the strictest default; `post_attention` schema and orchestration skill text | — (started 2026-11-15) |
| 2 | Native WPF look | — (started 2026-11-15) |
| 3 | Attachments: daemon storage and fetch, web upload, desktop drag-and-drop and file mode | A12, A9 |
| 4 | Web UI: Focus refs and navigation, file preview sheet, attach button | A11, step 1 |
| 5 | Output previews: web and WPF transcripts | A13, A11, step 1 |
| 6 | WPF Focus view with refs, preview pane and attachments | A9, steps 1–5 |
