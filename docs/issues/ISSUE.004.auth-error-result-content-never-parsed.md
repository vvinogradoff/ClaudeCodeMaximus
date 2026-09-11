# ISSUE.004: Auth-error detection (FR.12.16) never fires — `result` event error text was never parsed

## Symptom

A turn fails with a 401 (`Failed to authenticate. API Error: 401 Invalid authentication
credentials`), but:

- No "Re-authenticate" / Remove (✕) button ever appears in the command bar, even though
  `HasAuthError` (FR.12.16) is supposed to be set whenever a `result`/`system` error
  event's content contains that substring.
- The error text itself does not reliably appear as a system message in the session
  either.

## Root Cause

`ClaudeProcessManager.ParseResultEvent` (`code/TessynDesktop.Core/Services/ClaudeProcessManager.cs`)
builds the `result` stream event's `Content` exclusively from an `"errors"` array or a
singular `"error"` property:

```csharp
if (root.TryGetProperty("errors", out var errorsArr) && errorsArr.ValueKind == JsonValueKind.Array)
    ...
else if (root.TryGetProperty("error", out var errMsgEl))
    errorMsg = errMsgEl.GetString();
```

Claude Code CLI's `stream-json` `result` event puts its text — for **both** success and
"error during execution" (e.g. auth failures) — in the same `"result"` string field used
for normal completions (confirmed by `RunPrintModeAsync`/`ClaudeAssistService`, which
already parse `"result"` for the equivalent `--output-format json` single-shot call).
Neither `"errors"` nor `"error"` exist on an `error_during_execution` result, so `errorMsg`
stayed `null`, `Content` stayed `null`, and every downstream consumer that gates on
`!string.IsNullOrWhiteSpace(evt.Content)` — including the FR.12.16 auth-marker check in
`TurnController.HandleStreamEvent` — never ran.

The `"errors"` array branch is not dead code in general (some non-execution failures do
use it), but it does not cover the auth-failure case this feature was built for.

## Fix (implemented)

`ParseResultEvent` now falls back to the `"result"` string property when `is_error` is
true and neither `"errors"` nor `"error"` yielded a message:

```csharp
if (string.IsNullOrEmpty(errorMsg)
    && root.TryGetProperty("result", out var resultEl) && resultEl.ValueKind == JsonValueKind.String)
    errorMsg = resultEl.GetString();
```

This is a pure root-cause fix in the shared parser — no changes were needed in
`TurnController`'s auth-marker check itself, since both its persistence-side and
UI-dispatch-side switches already keyed off `evt.Content`/`evt.IsError`.

## Related change (explicit UX request — currently always-visible)

While this fix was outstanding, the "Re-authenticate" button's
`IsVisible="{Binding HasAuthError}"` binding was removed (`SessionView.axaml`) so the
button stays reachable as a manual escape hatch regardless of whether detection fires.
This has been toggled back and forth once already (see "Deployment gotcha" below for why
that first attempt looked like it didn't work) — as of now it is **always-visible**. Flip
it back to `IsVisible="{Binding HasAuthError}"` once a live 401 has confirmed the detection
fix actually works end-to-end in a running app that has picked up the fix (see below).

## Deployment gotcha (important — read before concluding this fix "doesn't work")

The app runs from `[Publish]/`, which `SelfUpdateService` only refreshes when the app is
**fully closed and relaunched** — the main DLL is locked while running, so `Initialize()`'s
startup copy skips it, and the real copy happens via a background script spawned on
`desktop.Exit`. If the app has been running continuously since before this fix (or any
other recent change) was built, `[Publish]` can silently run a build that is **days to
weeks old**, with none of the intervening fixes active — this looks exactly like a
regression ("the button isn't there", "the context menu isn't there") even though the
source and the latest `bin/Debug` build are correct. Confirmed once by comparing
`[Publish]/TessynDesktop.dll`'s last-write time against the fresh `bin/Debug` build's — a
12-day gap, and attempting to overwrite the `[Publish]` copy while the app was running
failed with "Device or resource busy," proving the lock.

**Before debugging a "missing feature" as a code regression, compare
`[Publish]/TessynDesktop.dll`'s timestamp against a fresh build's, and if it's stale, close
and relaunch the app before concluding anything is actually broken.**

## Status

Fixed — build clean, 221/221 tests pass. The Re-authenticate flow itself (unrelated to the
`ParseResultEvent` fix — it doesn't depend on `HasAuthError` detection) was manually
exercised successfully. The `ParseResultEvent`/`"result"`-field fallback that this issue is
actually about has **not** yet been confirmed against a live 401 in a build the running app
has actually picked up — that requires closing/relaunching the app (per the deployment
gotcha above) and then a real revoked-token repro.
