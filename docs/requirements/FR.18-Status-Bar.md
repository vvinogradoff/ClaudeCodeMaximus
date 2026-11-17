> Area file of the requirements (index: [README](README.md); old IDs and where they went: [ID-MAP](ID-MAP.md)). The text below is the unchanged existing text of this area; the batch of 2026-11-17 is at the end.

### FR.18 — Status Bar (Plan Usage & Model Pricing)

A narrow status bar at the very bottom of the main window provides at-a-glance information about the selected model's pricing and the active profile's usage against plan limits.

**FR.18.1 — Placement:** A narrow border (~22 px) is docked to the very bottom of the main window, full-width, above the OS taskbar. It is always visible, independent of which session (if any) is selected.

**FR.18.2 — Left section — model pricing:** Displays the currently selected session's model ID and its Anthropic pricing in the format:
```
<modelId>  ·  in $X.XX / out $Y.YY per 1M
```
For Ollama (local) models, the pricing is replaced with `· local`. When no session is selected or "Default" model is chosen the label is empty.

**FR.18.3 — Right section — usage bars:** Two thin progress bars, stacked vertically and pixel-adjacent (no gap between them):
- **Top bar** — 5-hour rolling window utilization (`five_hour.utilization`). Resets on the schedule supplied by the API.
- **Bottom bar** — 7-day rolling window utilization (`seven_day.utilization`). Resets on the weekly schedule supplied by the API.

Each bar has a text label overlaid in the center showing utilization % and reset time: `5h: 42%  resets 10:49` / `7d: 4%  resets Thu`. The fill color is dark grey; the track is the app background.

**FR.18.4 — Usage data source:** Usage is fetched from the Anthropic OAuth usage endpoint:
```
GET https://api.anthropic.com/api/oauth/usage
Authorization: Bearer <access_token>
anthropic-beta: oauth-2025-04-20
```
The access token is read from the active profile's `.credentials.json` file (`claudeAiOauth.accessToken`). For the default profile the file is at `~/.claude/.credentials.json`; for named profiles it is at `%APPDATA%\TessynDesktop\profiles\<profileId>\.credentials.json`.

A plan without a cap on a given window (e.g. an Ultra-tier plan has no 5-hour session limit) reports that window as `"five_hour": null` (a JSON null, not an omitted key). `ClaudeUsageService.ParseUsage` treats a missing or null `five_hour`/`seven_day` as "0%, no limit" for that window alone — it never lets one capless window discard an otherwise-valid reading for the other window. The parse only fails (returns no usage data) if *neither* `five_hour` nor `seven_day` is present at all.

**FR.18.5 — Failure behaviour (token rejection):** If the endpoint returns HTTP 401 (token expired or invalid), the usage fetch stops and is not retried for the remainder of the app session. Any previously cached usage data remains visible. If no cached data is available the bars are hidden entirely (`HasUsageData = false`).

**FR.18.6 — Polling:** Usage is refreshed once immediately when the active profile changes (session switch) and then every 5 minutes while the app is running. Each poll reads the access token fresh from disk (the token may have been refreshed on disk by Claude Code in the meantime).

**FR.18.7 — Model catalog (curated static list):** The model dropdown (FR.12.3) now uses a built-in curated catalog instead of a dynamic CLI query. The catalog ships with correct model IDs, display names, and per-1M pricing:

| Model ID | Display Name | In ($/1M) | Out ($/1M) |
|---|---|---|---|
| claude-fable-5 | Fable 5 | 10.00 | 50.00 |
| claude-opus-4-8 | Opus 4.8 | 5.00 | 25.00 |
| claude-opus-4-7 | Opus 4.7 | 5.00 | 25.00 |
| claude-opus-4-6 | Opus 4.6 | 5.00 | 25.00 |
| claude-sonnet-5 | Sonnet 5 | 3.00 | 15.00 |
| claude-sonnet-4-6 | Sonnet 4.6 | 3.00 | 15.00 |
| claude-haiku-4-5-20251001 | Haiku 4.5 | 1.00 | 5.00 |

Ollama models are still discovered live (FR.12.13) and appended after the Anthropic entries. They carry no price info (`InputPricePerMillion = 0`, `OutputPricePerMillion = 0`).

**FR.18.8 — Cache staleness on profile selection:** Each time the active profile changes (the same trigger points as FR.18.6 — selecting a different session, or changing the Profile selector within a session), the cached usage snapshot for the newly active profile, if any, is evaluated against the reset timestamps already supplied with it (`five_hour.resets_at`, `seven_day.resets_at`) before being shown. If the current time is at or past `FiveHourResetsAt`, that rolling window has necessarily turned over since the snapshot was captured, so the 5-hour utilization is shown as 0% (severity reset to `"normal"`) instead of the last-known percentage. The 7-day window is evaluated independently against `SevenDayResetsAt` the same way — a stale window on one side never forces the other to zero. The status bar omits the "resets …" time suffix for whichever window was just zeroed this way, until a fresh fetch supplies a current reset schedule. This complements FR.18.5: previously cached data "remained visible" indefinitely after a 401 with no further correction; now reselecting that profile later self-heals a stale non-zero reading back to 0% instead of continuing to show a number already known to be wrong. Implemented by `ClaudeUsageStalenessService` (`IsFiveHourStale`/`IsSevenDayStale`/`ZeroStaleWindows`), invoked from `ClaudeUsageService.SetActiveProfile` and reused by `MainWindowViewModel.UpdateStatusBarUsage` for the reset-time-suffix suppression.

---
