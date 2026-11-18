# TC.SD.01 — Focus item sound

Related requirements: FR.22.1–FR.22.6. Related use cases: [UC.SD.01](/docs/usecases/UC.SD.01-Focus-Item-Sound.md).

**Legend.** AUTOMATED = a Core or WPF integration test (xUnit) or a WPF end-to-end test (`code/TessynDesktop.WPF.E2E.Tests`); the intended class is named in each case.

## Summary

| TC | Title | Kind | UC | FR |
|---|---|---|---|---|
| TC.SD.01 | New item raises the sound | AUTOMATED (`FocusItemSoundNotifierTests`) | UC.SD.01 | FR.22.1 |
| TC.SD.02 | Update, resolve, dismiss and turn end are silent | AUTOMATED (`FocusItemSoundNotifierTests`) | UC.SD.02 | FR.22.1 |
| TC.SD.03 | Burst throttle (daemon) | AUTOMATED (`FocusItemSoundNotifierTests`) | UC.SD.03 | FR.22.3 |
| TC.SD.04 | Session switch off | AUTOMATED (`FocusItemSoundNotifierTests`) | UC.SD.04 | FR.22.2 |
| TC.SD.05 | Host fallback and ack | AUTOMATED (`NotificationPresenceTests`) | UC.SD.05 | FR.22.6 |
| TC.SD.06 | Client plays and acks once per item | AUTOMATED (`FocusItemSoundTests`) | UC.SD.01 | FR.22.6 |
| TC.SD.07 | Client burst throttle, option, replay | AUTOMATED (`FocusItemSoundTests`) | UC.SD.03, UC.SD.04 | FR.22.2, FR.22.3 |
| TC.SD.08 | Wire event | AUTOMATED (`NotificationWireTests`) | UC.SD.01 | FR.22.6 |
| TC.SD.09 | Setting text and throttle default | AUTOMATED (`ToolbarViewModelTests`, `SessionSoundServiceTests`) | — | FR.22.2, FR.22.3 |

## Cases

- **TC.SD.01-1 Every kind:** posting one new item of each kind (`critical`, `ask`, `todo`, `alert`, `decided`) raises the sound once per item (with the throttle window passed between them). The `notify_user` capture counts as an `ask`.
- **TC.SD.02-1 Update:** re-posting an open item (same key) raises nothing.
- **TC.SD.02-2 Resolve:** `resolve_attention`, `MarkAnsweredByUser` and `CloseByUser` raise nothing.
- **TC.SD.02-3 Turn end:** a `TurnCompleted` of the session raises nothing (nothing in the daemon listens to it for sound).
- **TC.SD.03-1 Burst:** one call with three created items, and a second session posting 1 s later, raise one sound; after the window a new item raises another.
- **TC.SD.04-1 Switch off:** with `PlayCompletionSound == false` on the owning session a new item raises nothing and publishes no event.
- **TC.SD.05-1 Not presented:** the host plays at once. **TC.SD.05-2 Presented and acknowledged:** the host stays silent. **TC.SD.05-3 Presented, no ack:** the host plays after the ack timeout.
- **TC.SD.06-1 Once per item:** `focus.itemCreated` plays once and sends one `notification.ack { nodeId }`; `turn.completed` plays nothing and acks nothing.
- **TC.SD.07-1 Burst:** two events inside the window play once and ack twice. **TC.SD.07-2 Option off:** nothing played; the ack is still sent. **TC.SD.07-3 Replay:** an event older than 30 s plays nothing.
- **TC.SD.08-1:** an item created in the daemon yields a `focus.itemCreated` notification with `nodeId`, `itemId` and `kind` on the local stream, and the relay filter drops it.
- **TC.SD.09-1:** the command bar toggle tooltip reads "Play a sound when a new Focus item appears in this session." and its automation name "Focus item sound". **TC.SD.09-2:** a stored throttle of 180 reads as 3.
