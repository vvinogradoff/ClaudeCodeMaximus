# ISSUE.005: Profile Rename/Delete moved from a dropdown context menu to buttons (nested-popup risk)

## Symptom

FR.12.19 (Profile Rename/Delete) was implemented as a right-click context menu on
individual entries in the Profile `ComboBox`'s dropdown. The user reported it was "not
there at all" and had apparently never worked since it was first built — no error, just
never observably functional in a live session.

## Investigation

Two separate causes were in play, and it's important not to conflate them:

1. **Stale deployment (confirmed, unrelated to this issue's fix):** the running
   `[Publish]` build was ~12 days out of date — see ISSUE.004's "Deployment gotcha"
   section. This alone could explain "not there" for *recent* changes, but the
   context-menu code had been in place since well before that stale snapshot, so it does
   not fully explain a feature that "never worked since it was built."

2. **Nested-popup fragility (the actual design risk):** the `ContextMenu` was attached to
   a `TextBlock` inside the `ComboBox.ItemTemplate` — i.e. a `Popup` (`ContextMenu`) opened
   from inside another already-open `Popup` (the `ComboBox`'s own dropdown). This is a
   documented fragile area in Avalonia: right-click / pointer-press handling around nested
   popups has several open issues upstream (e.g. AvaloniaUI/Avalonia#2921 "Popup closed
   when contained ComboBox closes", #5101 "Right-clicking popup will open parent
   ContextMenu in top-left screen corner"). It could not be safely reproduced/tested in
   this session without risking a second app instance writing to the same
   `appsettings.json`/session files as the user's live one, so it was never conclusively
   proven — but it is a strong, well-documented candidate for exactly this kind of
   "silently never opens" behavior.

## Resolution

Rather than debug an inherently fragile nested-popup interaction, Rename/Delete were moved
out of the dropdown entirely:

- `ProfileController.CanManageSelectedProfile` (`_selectedProfileIndex > 0`) drives
  visibility of two new plain buttons, **Rename** and **Delete**, in the command bar next
  to Re-authenticate — the same proven, non-popup pattern already used successfully for
  Re-authenticate and the auth-error-gated ✕ Remove button.
- They act on `AvailableProfiles[SelectedProfileIndex]` (the *currently selected* profile),
  not an arbitrary dropdown row under the cursor. This is a real capability change from the
  original context-menu design (which could target any row without first selecting it) —
  accepted as the tradeoff for a design that reliably works.
- `SessionView.axaml.cs`'s `OnProfileContextMenuOpening`/`OnProfileContextMenuClicked` and
  the `ComboBox.ItemTemplate`'s `ContextMenu` were removed. The `IsSystem`-driven grey
  `Foreground` styling on the Default entry (`BoolToSystemProfileForegroundConverter`) was
  kept — it's a plain data-bound property on a `TextBlock` inside a `DataTemplate`, not a
  popup, so it carries none of the same risk.

## Notes / Limitations

- The existing FR.12.18 ✕ Remove button (auth-error-gated) and the new always-visible
  Delete button now overlap in what they do (both remove the selected profile) — kept both
  since the ✕ button's gating condition serves a distinct recovery-flow purpose (FR.12.16
  guiding the user straight to fixing a broken profile) and removing it wasn't requested.

## Status

Fixed (redesigned) — build clean, 226/226 tests pass.

**Update:** after restarting onto a current build (resolving the ISSUE.004 stale-deployment
gotcha), the user confirmed the original right-click context menu genuinely does not open —
validating the nested-popup hypothesis above, not just the stale-deployment one. Bringing
the right-click interaction back is explicitly **not urgent** (the button-based replacement
covers the functionality) and has not been attempted. If revisited, investigate whether
Avalonia's `ContextRequested` event or a manually-positioned top-level `Popup` (anchored to
the `ComboBox` itself rather than nested inside its dropdown) can work around the nested-
popup limitation, rather than retrying the original `TextBlock.ContextMenu`-inside-
`DataTemplate` approach as-is.
