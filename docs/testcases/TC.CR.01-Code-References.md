# TC.CR.01 — Code references: session suggestions for `#`

Related requirements: [FR.7.13, FR.7.14](/docs/requirements/FR.07-Code-Reference-Autocomplete.md), [FR.4.8](/docs/requirements/FR.04-Application-Configuration.md); wire: tdcp-spec 10.A12. Use cases: [UC.CR.01](/docs/usecases/UC.CR.01-Code-References.md).

**Legend.** UNIT = xUnit in `TessynDesktop.Tests` (parser, module over a fake registry and fake code index); CONTRACT = `ComposerWireTests` with golden files; VM = WPF view-model tests; E2E = `tools\e2e\Run-WpfE2E.ps1`.

**Fixture S (sessions).** Project **P** (Directory Node, path `C:\p`): group **G1** with sessions `Alpha build` (current session), `Alpha tests`, `Beta alpha`, `alpha fav` (favourite); group **G2** with `Alpha docs`; loose sessions (no group) `Alpha loose`, `Gamma`. Project **Q**: `Alpha web`, `Alpha qa`. Workers: `Alpha worker` (SupervisorNodeId set) and `Alpha deep` (OrchestrationDepth 1) in P/G1.

| TC | Title | Kind | UC | FR |
|---|---|---|---|---|
| TC.CR.01 | Parser OFF: `see #alpha` caret at end gives mode `session`, triggerStart at the `#`, triggerLength to the caret | UNIT | UC.CR.01 | FR.7.14 |
| TC.CR.02 | Parser: `#` at line start and after whitespace triggers; `a#b` (no whitespace before) does not; `##x` stays `file`; `C:\x` stays `path` | UNIT | UC.CR.01, UC.CR.05 | FR.7.14, FR.7.2, FR.7.12 |
| TC.CR.03 | Spaces in the query: `#alpha te` gives query `alpha te` and matches `Alpha tests`; `#alpha te` with caret mid-text uses text up to the caret only | UNIT | UC.CR.02 | FR.7.14 |
| TC.CR.04 | Empty query (`#` alone) answers mode `none` and no items | UNIT | UC.CR.02 | FR.7.14 |
| TC.CR.05 | A line break between the `#` and the caret: `none` (`#alpha\nte`, caret at end) | UNIT | UC.CR.02 | FR.7.14 |
| TC.CR.06 | No session name matches (`#zzz`, `#alpha zz`): answer `none`, no items (the popup closes) | UNIT, VM | UC.CR.02 | FR.7.14 |
| TC.CR.07 | Matching is a case-insensitive substring: `#ALPHA`, `#pha`, `#LPH` all match `Alpha tests`; `matchStart` / `matchLength` mark the first occurrence | UNIT | UC.CR.01 | FR.7.14 |
| TC.CR.08 | Tier order with fixture S, query `alpha`, current session `Alpha build` in P/G1: tier 1 (G1: `Alpha tests`, `alpha fav`, `Beta alpha`) before tier 2 (P: `Alpha docs`, `Alpha loose`) before tier 3 (Q: `Alpha web`, `Alpha qa`) | UNIT | UC.CR.01 | FR.7.14 |
| TC.CR.09 | Within tier 1 for `alpha`: starts-with names before contains (`Beta alpha` last), the favourite `alpha fav` before `Alpha tests` among the starts-with, then tree order | UNIT | UC.CR.01 | FR.7.14 |
| TC.CR.10 | Threshold: tiers 1 and 2 give 5 results (fixture S): tier 3 is added (7 results). With 10 or more results in tiers 1 and 2 (add 5 matching sessions to P): no session of Q appears. Exactly 9 in tiers 1 and 2: tier 3 added; exactly 10: not added | UNIT | UC.CR.01 | FR.7.14 |
| TC.CR.11 | The total never exceeds 15, also with 30 matching sessions in P; the first 15 follow the tier order | UNIT | UC.CR.01 | FR.7.14 |
| TC.CR.12 | Workers excluded: `Alpha worker` (SupervisorNodeId) and `Alpha deep` (OrchestrationDepth 1) never appear in any tier | UNIT | UC.CR.03 | FR.7.14 |
| TC.CR.13 | The current session is excluded (`Alpha build` is not offered for `#alpha build`: answer `none`) | UNIT | UC.CR.03 | FR.7.14 |
| TC.CR.14 | A session loose under the Directory Node counts as one group: for a current session that is loose, other loose sessions of P are tier 1 and grouped sessions of P are tier 2; for a grouped current session loose sessions are tier 2 | UNIT | UC.CR.01 | FR.7.14 |
| TC.CR.15 | Row and insert format: `kind` `session`, `label` = name, `detail` = `P / G1` (project label and group path; loose session: project label only), `insertText` = `session "Alpha tests" (<shortid>)` with the id as `list_sessions` shows it; two sessions whose first 8 id characters match get ids lengthened until unique | UNIT, CONTRACT | UC.CR.01 | FR.7.14 |
| TC.CR.16 | No index wait: in `session` mode `indexReady` is true, the fake code index records no `GetOrCreateIndexAsync` / lease `Touch` call, and the answer comes while the fake index would still be building | UNIT | UC.CR.01 | FR.7.14 |
| TC.CR.17 | ON keeps symbols: with `CodeReferencesEnabled` true, `#Fo` answers mode `symbol` with the symbol items (golden `autocomplete.suggest.symbol`, unchanged) and never offers a session | UNIT, CONTRACT | UC.CR.04 | FR.7.13 |
| TC.CR.18 | OFF keeps `##` and drive paths: with the setting false, `##logo` answers `file` over a tree with `logo.png`, `intro.mp4`, `README.md`, `.env`, `data.csv`, `index.html`, `notes.txt` and `Logo.cs` (all findable by file name), and `C:\<temp dir>\` answers `path` with the entries; the same answers with the setting true | UNIT, CONTRACT | UC.CR.05 | FR.7.13, FR.7.9, FR.7.12 |
| TC.CR.19 | Index created while OFF does not parse symbols: `CodeIndex` built with symbol parsing off holds all files (`SearchFiles` finds `Foo.cs`) and no symbols (`SearchSymbols("Foo")` empty); the same tree built with it on holds the class `Foo`. An index created while ON and still held keeps its symbols after the setting turns off (not dropped) and is not used for `#` | UNIT | UC.CR.04 | FR.7.13 |
| TC.CR.20 | The setting: default false for an `appsettings.json` without the member; `settings.update` `general` `codeReferencesEnabled: true` persists across a settings reload and shows in `settings.get`; the next `autocomplete.suggest` (same daemon, no restart) changes from `session` to `symbol` and back; the `general` DTO property list and patch whitelist contain the member | UNIT, CONTRACT | UC.CR.04 | FR.7.13, FR.4.8 |
| TC.CR.21 | Settings > General shows the checkbox **Code references** (UI Automation name `Code references`) with the hint; ticking it makes the tab dirty, `BuildPatch` carries only `codeReferencesEnabled`, Save stores it; closing with it changed and unsaved asks (FR.4.7) | VM, E2E | UC.CR.04 | FR.4.8 |
| TC.CR.22 | WPF client: the pre-filter lets `see #alpha te` (caret after the space) through to the daemon; the popup shows `session` rows with the kind text `session`; no "Building the code index" state ever appears for mode `session`; accepting replaces `[triggerStart, +triggerLength)` and puts the caret behind the inserted text | VM | UC.CR.01, UC.CR.02 | FR.7.14, FR.7.8 |
| TC.CR.23 | WPF E2E: with the setting off and sessions from fixture S in the test daemon, typing `see #alpha te`, Down, Enter leaves the message box holding `see session "Alpha tests" (<shortid>)`; Esc closes the popup | E2E | UC.CR.01, UC.CR.02 | FR.7.14, FR.7.8 |
| TC.CR.24 | WPF E2E: the same box typed with the setting turned on in Settings (no restart) opens symbol suggestions for `#Fo`, as `WpfComposerCompletionE2ETests` does today | E2E | UC.CR.04 | FR.7.13 |

## Contract goldens

- New `autocomplete.suggest.session.json` (rows of tiers 1 to 3, with `kind`, `detail`, `insertText`, `indexReady: true`).
- `settings.get` / `settings.update` goldens for `general` gain `codeReferencesEnabled`.
- `autocomplete.suggest.{file,symbol,none}.json` unchanged.

## Manual

1. **UC.CR.01** — With Code references off, type `#` and part of a session name in a real session; accept a row; send, and ask the agent to read the session.
2. **UC.CR.04** — Tick **Code references**, Save, and see C# symbols for `#` in a session without restarting; clear it and see sessions again.
