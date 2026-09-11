# Browser Control via Chrome DevTools MCP (CDP / attach mode) — Implementation Plan

> Status: **draft for Architect review**. Not a requirement yet. No code beyond the
> already-committed experimental `--chrome` toggle (commit `4caf4df`) should land until
> the FR is signed off. Plans are working documents — this file is intentionally uncommitted.

## Locked decisions (from the Architect)

| Topic | Decision |
|---|---|
| Server | Inject **`chrome-devtools-mcp`** (CDT) via the existing `--mcp-config` injection |
| Connection | **Attach** to an already-running Chrome (not MCP-launched). "I can start my browsers a special way." |
| Profile | **Dedicated** Chrome profile dir — the Claude-controlled browser has its own history/profile |
| Window | **Headful + minimized** |
| Driver | **Playwright** (see §6 — applies to the Phase 2 native server, not Phase 1) |

## 1. How CDT attach mode actually works (verified against the CDT MCP docs)

`chrome-devtools-mcp` is a stdio MCP server. Key flags:

- **`--browser-url` (`-u`) `http://127.0.0.1:<port>`** — attach to a running Chrome's DevTools
  endpoint. (Alt: `--ws-endpoint`/`-w` for a raw ws:// URL.)
- `--headless` (default `false` → headful). **Ignored in attach mode** — the browser is
  already running; the MCP only connects.
- `--user-data-dir`, `--isolated`, `--channel`, `--executablePath` — **only apply when the MCP
  launches the browser**, i.e. NOT in attach mode.
- `--experimentalPageIdRouting` — needed only if one server instance serves concurrent
  sessions. Guidance: **one server per conversation**.

**Consequence that shapes everything below:** in attach mode, "headful", "minimized", and
"dedicated profile dir" are **not** CDT-MCP flags — they are determined by **how the Chrome
process is launched**. Tessyn (or the user) launches Chrome; CDT-MCP just attaches over the port.

The under-the-hood driver of `chrome-devtools-mcp` is **Puppeteer**, not Playwright — see §6.

## 2. The attach workflow (Phase 1)

1. **Launch Chrome once, per Tessyn profile**, with a dedicated data dir + a debug port,
   minimized:
   ```
   chrome.exe --remote-debugging-port=<port> ^
              --user-data-dir="%AppData%\TessynDesktop\chrome-profiles\<profileId>" ^
              --start-minimized
   ```
   This Chrome has its own history/cookies/profile (matches "own history and profile dir").
   Because it's a normal, user-launched Chrome, this is also the **least bot-detectable** CDP
   posture (see §7).

2. **Tessyn injects a second MCP server** into the per-session `--mcp-config` (alongside `cmx`):
   ```json
   {
     "mcpServers": {
       "cmx": { "type": "http", "url": "http://127.0.0.1:<cmxPort>/…", "headers": { … } },
       "chrome-devtools": {
         "command": "npx",
         "args": ["-y", "chrome-devtools-mcp@latest", "--browser-url=http://127.0.0.1:<port>"]
       }
     }
   }
   ```
   `<port>` is the port of the Chrome launched for **this session's active Tessyn profile**.

3. Claude (running headless `-p`, with MCP tools) now has ~29 DevTools tools operating on the
   attached Chrome — navigate, click, type, read DOM/console/network, screenshot, run JS,
   upload files, etc.

## 3. Integration point in Tessyn

The injection already exists — `AgentMcpServer.EnsureConfigFileAsync` →
`BuildMcpConfigJson(agentToken)` writes the per-node `--mcp-config` with the single `cmx`
entry. Extend it to **conditionally add the `chrome-devtools` entry** when browser control is
enabled for the session.

- Reuse the **existing 🌐 toggle** (committed in `4caf4df`) as the on/off switch. Its meaning
  changes from "append `--chrome`" to "inject the `chrome-devtools` MCP server (attach mode)".
  The raw `--chrome` experiment can be retired or kept behind a hidden setting.
- `BuildMcpConfigJson` needs the **resolved browser-url for the session's active profile**, so
  the config write must know the active profile (available via `ProfileController` /
  `SessionNodeModel.ProfileName`). This is a signature/threading change through
  `EnsureConfigFileAsync`.
- Gate identical to `cmx`: only inject for **tool-capable** models (skip Ollama tool-incapable
  models) and when the feature setting is enabled.

## 4. Profile ↔ Chrome mapping

Parallel to how Tessyn already isolates `CLAUDE_CONFIG_DIR` per profile:

- **Per Tessyn profile:** a dedicated `chrome-profiles\<profileId>` data dir **and** a stable
  debug port (e.g. `9222 + stableIndex`). Persist the port with the profile.
- **Default profile:** its own reserved dir + port.
- Tessyn writes `--browser-url=http://127.0.0.1:<port>` into the injected config based on the
  session's active profile, so "the Claude browser matches my active Tessyn profile".
- **Launch helper (recommended):** a per-profile "Launch browser" action that starts Chrome with
  the right port + data dir + minimized — mirrors the existing per-profile `claude auth login`
  `.bat` generation (requirements.md §FR.12.7 / :427). Gives the user the "special way" to start
  browsers without memorising flags.

**Concurrency caveat:** CDT-MCP guidance is one client per browser. Two sessions on the **same
profile** would both attach to that profile's single Chrome and could contend. Options: (a)
accept it (per-profile browser, serialize), or (b) go **per-session** data-dir/port instead of
per-profile. Recommend starting per-profile (matches the ask); revisit if contention bites.

## 5. Visibility: headful + minimized, and mid-flight reveal

Hard constraint: **headless↔headful cannot be flipped on a live Chrome** — it's fixed at launch.
Since we launch **headful-but-minimized**, the window already exists and can be revealed:

- **Phase 1 (CDT-MCP):** reveal is **manual** — user clicks the taskbar icon; some interactions
  (navigation/focus) may surface it. CDT-MCP does not expose a "restore my window" tool.
- **Phase 2 (native server):** Tessyn/Claude-controlled reveal via CDP
  `Browser.getWindowForTarget` + **`Browser.setWindowBounds({ windowState: 'minimized' | 'normal' })`**.
  Wire this to the 🌐 toggle (or a dedicated command) so visibility can be flipped **mid-flight**
  — this is the concrete mechanism that satisfies "ideally trigger visibility midflight".

## 6. Puppeteer vs Playwright — reconciling the two instructions

"Inject chrome-devtools-mcp" and "use Playwright" don't combine in one component:
`chrome-devtools-mcp` ships its **own Puppeteer** transport; you don't get to swap in Playwright.
So the two decisions map cleanly onto **two phases**:

- **Phase 1 — CDT-MCP (Puppeteer under the hood).** Fastest path to working. Tessyn writes ~5
  lines of MCP config; no browser driver dependency in the C# codebase. Delivers full automation
  parity. **No Playwright here.**
- **Phase 2 — Tessyn-native browser MCP server using Playwright for .NET.** Replaces/augments the
  off-the-shelf server when we want host-owned control: **`ConnectOverCDPAsync("http://127.0.0.1:<port>")`**
  to attach to the same user-launched Chrome, plus Tessyn-specific tools (`set_visibility`,
  per-profile lifecycle, window bounds). This is where **Playwright** lives.

If it turns out we only ever need Phase 1, "use Playwright" is moot; if we want the reveal
button and tight lifecycle control, Phase 2 (Playwright) is the vehicle. Flagging so the
Architect can confirm the intent.

## 7. Answering the standing questions

- **Control parity:** yes — CDT/CDP is a superset of the extension's automation surface.
- **Bot detectability:** CDP-driven browsers are detectable by default (`navigator.webdriver`,
  automation infobar, CDP fingerprints). **Attach-to-a-user-launched Chrome is the least
  detectable CDP posture** — the browser itself is an ordinary Chrome; only the control channel
  differs, and no `--enable-automation` is set. This is exactly the mode we chose. Hardened
  anti-bot systems (Cloudflare/DataDome/etc.) may still fingerprint CDP; a real user-data-dir +
  `--disable-blink-features=AutomationControlled` on the launch reduces this further.

## 8. Phasing & effort

- **Phase 1 (small):** extend `BuildMcpConfigJson` + thread active-profile/port; repurpose the 🌐
  toggle; add the per-profile launch helper; per-profile port persistence. Validate the
  `-p` + MCP + attached-Chrome loop end to end.
- **Phase 1.5 (small):** UX polish — status ("browser attached on :<port>"), error when no Chrome
  is listening on the expected port.
- **Phase 2 (medium):** native Playwright-for-.NET browser MCP server (sibling to `AgentMcpServer`),
  `set_visibility` via `Browser.setWindowBounds`, lifecycle tied to the session; toggle drives
  reveal/hide mid-flight.

## 9. Open questions for the Architect

1. New requirement number + is this an FR under §FR.12 (command bar) or a new §FR (browser control)?
2. Per-profile vs per-session browser (concurrency, §4)?
3. Should Tessyn **launch** Chrome (launch helper) or purely rely on the user starting it?
4. Retire the raw `--chrome` experiment once CDT attach lands, or keep both behind settings?
5. Phase 2 now or later — i.e. is Tessyn-controlled mid-flight reveal required for v1, or is
   manual taskbar reveal acceptable to start?
