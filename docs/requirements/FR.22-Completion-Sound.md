> Area file of the requirements (index: [README](README.md); old IDs and where they went: [ID-MAP](ID-MAP.md)). The text below is the unchanged existing text of this area; the batch of 2026-11-17 is at the end.

### FR.22 — Session Completion Sound

A per-session, opt-in audible cue for the interactive command bar — the only turn-completion signal Tessyn gives, since neither the desktop toast nor Telegram announces turn completion any more (FR.21.5, ISSUE.012). It covers interactive turns only, deliberately (Architect, 2026-10-28): a turn started by the scheduler, by orchestration or from Telegram finishes without a sound, and a session that needs the user calls `notify_user` (FR.21.3, FR.21.24).

**FR.22.1 — Trigger point:** The sound fires when `TurnController.SendAsyncCore` clears the "Claude is thinking…" state on natural completion of the last concurrent send (the same point that stops the thinking timer and sets `IsBusy`/`IsRunning` false) — for both One-shot Mode (the spawned process exits) and Streaming Session Mode (`SendViaStreamingAsync` returns on the result event). It does not fire when the turn ends via user-initiated Stop (`OperationCanceledException` path) or any other non-natural termination.

**FR.22.2 — Per-session toggle:** `SessionNodeModel.PlayCompletionSound` (bool, default `true`), persisted like `IsStreamingMode`. Exposed via a bell-glyph (🔔) `ToggleButton` in the command bar, to the right of the Streaming Session Mode toggle. To fit three toggle buttons (`--chrome`, Streaming, Completion Sound) in the available space, all three are rendered as fixed-size squares (26×26, no padding) with reduced inter-button spacing and explicit `HorizontalContentAlignment`/`VerticalContentAlignment="Center"` (needed once `Padding="0"` is set, since Avalonia's default `ToggleButton` content alignment otherwise renders the glyph in the top-left corner of a fixed-size square), replacing their previous wider `Padding="7,2"` rendering.

**FR.22.3 — Per-session throttle:** No more than one sound per session within a configurable window (`AppSettingsModel.SessionCompletionSoundThrottleSeconds`, default 180 = 3 minutes). Enforced by `ISessionSoundService` (app-lifetime singleton) tracking last-played timestamp per `NodeId` in memory — a session that finishes multiple turns in quick succession (e.g. rapid follow-up prompts) is not spammed with sounds; a session that has been quiet for longer than the window is not blocked from playing.

**FR.22.4 — Sound source and playback:** `AppSettingsModel.SessionCompletionSoundFile` (string, default `%WINDIR%\Media\Windows Notify.wav`) names a `.wav` file. `ISessionSoundService` plays it via the Win32 `winmm.dll` `PlaySound` API (`SND_ASYNC | SND_FILENAME`) — chosen over `System.Media.SoundPlayer` or a NuGet audio package to avoid a project-reference change. Playback failures (missing file, no audio device) are logged and swallowed; they never fail or delay a turn.

**FR.22.5 — Sound picker:** Settings → Notifications gains a "Session completion sound" control group: a dropdown populated by `ISessionSoundService.GetAvailableSounds()` (every `.wav` under `%WINDIR%\Media\`, i.e. the Windows default sound library), a **Test** button that plays the selected sound immediately (bypassing the throttle), and a throttle-seconds numeric field bound to `SessionCompletionSoundThrottleSeconds`.

---
