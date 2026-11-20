# ID preservation map

IDs are never renumbered and never reused. This table is the contract of the restructure: where each existing section of `docs/requirements.md` (line numbers of heading rows at merge `97fdd0e`, the last commit before the split; the FR.25 text and the draft amendments were committed on master as `83ffe47` first) lives now. **Every ID keeps its exact text form.**

## Old sections → new files

State: **done** (the split is committed; `docs/requirements.md` is now the entry page). Verification output: `.wpf-work/486-docs.md`.

| Old heading (line) | ID range | New file |
|---|---|---|
| Project Overview (3), Anthropic T&C note (9) | — | `docs/requirements.md` entry page (stays) |
| FR.1 Session Tree (23) | FR.1.1–FR.1.16 (+ FR.1.17 from the draft) | FR.01-Session-Tree |
| FR.2 Session View (112) | FR.2.1–FR.2.5, FR.2.3.1–FR.2.3.5 | FR.02-Session-View |
| FR.3 Session Files (152) | FR.3.1–FR.3.6 | FR.03-Session-Files |
| FR.4 Application Configuration (184) | FR.4.1–FR.4.6 | FR.04-Application-Configuration |
| FR.5 Claude Code Process Management (208) | FR.5.1–FR.5.5 | FR.05-Claude-Process-Management |
| FR.6 Application Shell (222) | FR.6.1–FR.6.4 | FR.06-Application-Shell |
| **Non-Functional Requirements (239)**, which sits between FR.6 and FR.7 | NR.1–NR.5 | NR-Non-Functional |
| FR.7 Code Reference Autocomplete (259) | FR.7.x | FR.07-Code-Reference-Autocomplete |
| FR.8 Self-Update on Exit (307) | FR.8.x | FR.08-Self-Update |
| FR.9 Theme & Color Customization (321) | FR.9.1–FR.9.8 | FR.09-Theme-and-Colours |
| FR.10 Output Search (364) | FR.10.x | FR.10-Output-Search |
| FR.11 Session Instruction Toolbar (390) | FR.11.x | FR.11-Session-Instruction-Toolbar |
| FR.12 Input Command Bar & Model Selection (479) | FR.12.1–FR.12.29 | FR.12-Input-and-Composer |
| FR.13 Session Import (647) | FR.13.x | FR.13-Session-Import |
| FR.14 Scheduled Turns (750) | FR.14.x | FR.14-Scheduled-Turns |
| FR.15 Agent Orchestration (788) | FR.15.x | FR.15-Agent-Orchestration |
| FR.16 Model Tier Governance (917) | FR.16.x | FR.16-Model-Tier-Governance |
| FR.17 Token Capture in Non-Daemon Path (942) | FR.17.x | FR.17-Token-Capture |
| FR.18 Status Bar (952) | FR.18.x | FR.18-Status-Bar |
| FR.19 Streaming Session Mode (1002) | FR.19.x | FR.19-Streaming-Mode |
| FR.20 External MCP Server Registry (1039) | FR.20.x | FR.20-External-MCP-Registry |
| FR.21 Telegram Notification Bridge (1086) | FR.21.x | FR.21-Telegram-Bridge |
| FR.22 Session Completion Sound (1215) | FR.22.x | FR.22-Completion-Sound |
| FR.23 Focus View (1231) | FR.23.1–FR.23.19 | FR.23-Focus-View |
| FR.24 Session Tags (1465) | FR.24.1–FR.24.15 | FR.24-Session-Tags |
| FR.25 Web Client (1691) | FR.25.01–FR.25.40 | FR.25-Web-Client |
| FR.26 Link Fidelity… (1969) | FR.26.x | FR.26-Links-and-Previews |
| FR.31 Attachments (1986) | FR.31.x | FR.31-Attachments |
| Out of Scope (2011) | — | Out-of-Scope |
| Glossary Reference (2028) | — | `docs/requirements.md` entry page |

The amendment paragraphs of the FR.25 commit (FR.1.8 amendment, FR.1.17, FR.3.3 parse rule, the FR.11.6 compaction service text, FR.19.11 amendment) were already inside their areas in `requirements.md` and moved with them, unchanged.

## IDs reserved or newly allocated by this batch
| ID | Meaning | Note |
|---|---|---|
| FR.1.18–FR.1.27 | date switch, equal tabs, rows, worker count, squares, no dead space, tags scope, tree toolbar, resumable and husks, switch profile | FR.1.17 belongs to the draft |
| FR.2.6–FR.2.11 | input sources, assistant, system, user rows, selectable text, scrollbar | FR.2.6 was the number proposed in report 451 |
| FR.6.5–FR.6.8 | icon, version text, title bar, status bar | |
| FR.9.9–FR.9.11 | colours tab, recency (replaces FR.9.5), dropdown colours | |
| FR.9.12 | line spacing setting for the session output and the Focus view | 2026-11-17 |
| FR.1.28, FR.9.15, FR.9.16 | active workers badge; the closed dropdown keeps its colour on hover; badge colours | 2026-11-18 |
| FR.1.29, FR.9.17 | limits-error warning on session rows; its colour | 2026-11-18 |
| FR.11.14 | profile on the row | |
| FR.12.30–FR.12.34 | toggles, composer layout, Send later picks, pointer, catalogue refresh | |
| FR.14.13 | schedule picks | |
| FR.20.10, FR.21.25, FR.25.41 | settings through the Remote API | FR.25 ends at 25.40 |
| FR.23.20–FR.23.23 | selectable Focus text, Send answers placement, Ctrl+Enter, Closed in WPF | |
| FR.24.16–FR.24.18 | tag-area rule, Tags tab results, tag cloud font size | FR.24.16 was only a proposal in report 452, never in the docs |
| **FR.32.1–FR.32.8** | **new area** hosting and coexistence | FR.27–FR.30 reserved |
| FR.6.10 | monitor toggle in the title bar | 2026-11-18, amends FR.6.7 |
| **FR.27.1–FR.27.9** | **new area** embedded browser of the web preview windows (profile, navigation, new windows, controls, downloads, permissions, isolation, clear data, known limit) | 2026-11-18, reserved by the WPF plan |
| **FR.29.1–FR.29.13** | **new area** preview windows: monitor toggle, groups, patterns and matching, routing, group window, placement, chrome, pinned window, auto-close, monitors change, restart (windows reopen), file kinds, test seam | 2026-11-18, amends FR.23.18, FR.24.7, FR.26.3, FR.26.5, FR.26.6 |
| **FR.27.10** | typeable address in the browser bar | 2026-11-18, Architect feedback, amends FR.27.4 |
| **FR.29.14**, **FR.29.15** | file controls in the title bar; resizable edges | 2026-11-18, Architect feedback, amend FR.29.7 and FR.29.5 |
| UC/TC code PW | UC.PW.01–16, TC.PW.01–76 | no clash with the codes above |
| **FR.2.14** | readable Markdown tables (session output, preview pane and windows, Focus cards) | 2026-11-19, Architect feedback; UC.MT.01, TC.MT.01–05 (new code MT) |
| **FR.23.29** | references on expanded closed Focus items; a preview from a closed item is not tied to it | 2026-11-19, Architect feedback, amends FR.23.14, FR.23.18; UC.FC.04, TC.FC.11–13 |
| **FR.26.7** | relative paths in assistant entries are marked and open on Ctrl+click | 2026-11-19, Architect feedback, amends FR.26.5; UC.LK.07, TC.LK.26–35 |
| **FR.26.8** | HTML files are previewed rendered (static policy) with a Source toggle like markdown | 2026-11-19, Architect request, approved with option 1, Static (A31); amends FR.23.18, FR.29.5, FR.29.14, FR.29.18; UC.LK.08, TC.LK.36–53 |
| **FR.29.16** | placement with Snap (the actual rectangle) and monitor scaling | 2026-11-19, Architect feedback, amends FR.29.6, FR.29.11; TC.PW.71–76, UC.PW.04 A5 |
| UC/TC code MT | UC.MT.01, TC.MT.01–05 (Markdown tables) | no clash with the codes above |
| **FR.23.30** | file paths (absolute and relative) in Focus card titles and bodies are marked and open on Ctrl+click; `file.preview` with `itemId` (tdcp-spec 10.A11) | 2026-11-19, Architect feedback, amends FR.23.18, FR.23.25; UC.FX.03, TC.FX.23–32 |
| **FR.29.17–FR.29.26** | in-window docked previews on AvalonDock: docked mode, one panel per group and the default Preview panel, docking/tabs/floating/auto-hide, Pin and Auto-close, the `previewDock` layout per group, restart, mode changes, theme, the library, the test seam | 2026-11-19, Architect item 5, approved with six decisions; amends FR.29.1, FR.29.3, FR.29.4, FR.29.8–FR.29.11, FR.23.18, FR.27; UC.PW.17–24, TC.PW.77–103 (stage 1 built 2026-11-19, merge `6c1b968`) |
| **FR.6.11–FR.6.22** | the docked shell: the whole WPF UI as docked panels (Sessions, Recent, Tags, Session view, Focus, previews), default layout, compact chrome, the Panels menu, the chevron, Focus as a panel, composer, layout memory (`shellLayout`), Reset layout, keyboard, theme, test seam | 2026-11-19, Architect, option A, approved with seven decisions; amends FR.6.1–FR.6.3, FR.1.15, FR.1.19, FR.12.29, FR.23.12, FR.23.13, FR.23.21, FR.29.18, FR.29.19, FR.29.24; UC.DS.01–12, TC.DS.01–26 (new code DS) |
| **FR.6.23** | the Focus panel's header: the session's Delete, editable name, Running / Idle and star in the Focus panel's top row, same space as in the Session view header, 8 px bottom padding, Send answers at the right end | 2026-11-20, Architect (draft, awaiting approval); amends FR.6.16, FR.23.21, FR.1.25, FR.9.7; UC.DS.13, TC.DS.27–38 |
| NR.6, NR.7 | E2E running, stretching rule | |
| UC/TC codes | LP, TR, IN, CL, SH, TA, FC, HS | no clash with 15, 21, DL, FD, FV, IC, LC, RT, TG, WEB |

## Superseded
| ID | Status | By |
|---|---|---|
| FR.9.5 (three recency tiers 15/30/60 min) | **Superseded**; the ID stays with a pointer | FR.9.10 |
| FR.9.2 colour list (12 colours), FR.9.3 "palette dropped" sentence | **Amended / withdrawn** | FR.9.9 |
| FR.24.16 of report 452 (mini/full, persisted) | **Never entered; superseded before entry** | FR.24.16 (new rule) |
| FR.23.14 item 1 (Send answers in the list) | **Moved** | FR.23.21 |
| FR.23.14 "Not built yet" sentence for Closed and Decisions in WPF | **Obsolete** | FR.23.23 |
| FR.2.3.4 "You" label | **Amended** | FR.2.9 |
