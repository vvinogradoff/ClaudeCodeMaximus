# UC.SD.01 — Focus item sound

Related requirements: FR.22.1–FR.22.6 (Focus Item Sound), FR.23.4, FR.23.7 in [requirements](/docs/requirements/README.md). Related: [UC.FV.01](/docs/usecases/UC.FV.01-Focus-View.md).

## Actors

- **User** (the Architect) — hears the sound, turns it off per session.
- **Session** — posts Focus items (`post_attention`, `notify_user`).
- **Tessyn** — the daemon host and the WPF client.

## Preconditions (common)

- Agent tools are enabled; the sound file exists; the session's "Play a sound when a new Focus item appears" toggle is on unless a use case says otherwise.

---

## UC.SD.01 — A new item plays one sound

**Main flow:**
1. The Session calls `post_attention` with one item of any kind (`critical`, `ask`, `todo`, `alert` or `decided`).
2. Tessyn creates the item; the daemon publishes `focus.itemCreated`.
3. The WPF client (it registered the `sound` channel) plays the configured sound and acknowledges it (`notification.ack { nodeId }`); the host stays silent.

**Outcome:** exactly one sound for the new item.

## UC.SD.02 — Updates, resolutions and turn ends are silent

**Main flow:**
1. The Session re-posts an item with the same key (an update), then resolves it; the User dismisses another item; a turn of the session completes.
2. No sound plays for any of these.

**Outcome:** the sound means only "a new item".

## UC.SD.03 — A burst gives one sound

**Main flow:**
1. A Session posts three items in one call, and another Session posts an item within the throttle window (default 3 s).
2. One sound plays.

**Outcome:** a burst is one cue.

## UC.SD.04 — The session switch

**Main flow:**
1. The User turns the session's bell toggle off.
2. The Session posts a new item.
3. No sound plays (and no event is published); the item is in Focus as usual.

## UC.SD.05 — No client present, or a hung one

**Main flow:**
1. No client registered the `sound` channel: the host plays the sound at once.
2. A client registered it but never acknowledges: the host plays it after about 3 s.

**Outcome:** the sound is never lost, never doubled.
