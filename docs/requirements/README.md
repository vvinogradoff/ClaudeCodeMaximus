# TessynDesktop — Requirements (index)

> **Phase 1 state.** This folder is the **target structure** of the requirements. The old single file `docs/requirements.md` is still the source of the existing text and is **not changed yet** (decision A38 is pending: the main tree holds uncommitted draft edits to it). In phase 2 each area's existing text moves into its file here, with the staged batch text of 2026-11-17 added, and `docs/requirements.md` becomes a **short stable entry page** (project overview, the Anthropic note, this index) so every existing link to `/docs/requirements.md` keeps working.
> Files marked **staged** below currently hold only the batch text; the rest are created in phase 2.

## Rules
- IDs follow the `CLAUDE.md` scheme and are **never renumbered or reused**. [ID-MAP.md](ID-MAP.md) lists where every old section goes.
- One file per FR area: `FR.<NN>-<Slug>.md`. A section keeps its `### FR.<NN> — Title` heading and all its `FR.<NN>.<MM>` paragraphs.
- Amendments to an existing requirement are written as an **amendment table** at the top of the area file (existing ID, amended by, change) and as a **new ID** below, never as a silent edit. The old ID stays and points to the new one (FR.9.5 → FR.9.10).
- Use cases live in `docs/usecases/UC.<Code>.<NN>-*.md`, test cases in `docs/testcases/TC.<Code>.<NN>-*.md`, the same code in both.
- The wire protocol stays in `docs/refactoring/tdcp-spec.md`; a requirement names the field and points to the section.

## Index

| File | Area | Existing IDs | Batch 2026-11-17 adds | State |
|---|---|---|---|---|
| FR.01-Session-Tree | Session tree, left panel, Recent tab | FR.1.1–1.17 | FR.1.18–1.27 | staged |
| FR.02-Session-View | Session view, transcript rows | FR.2.1–2.5, 2.3.x | FR.2.6–2.11 | staged |
| FR.03-Session-Files | Session file format | FR.3.1–3.6 | — | phase 2 |
| FR.04-Application-Configuration | Application config | FR.4.1–4.6 | — | phase 2 |
| FR.05-Claude-Process-Management | Claude Code process | FR.5.1–5.5 | — | phase 2 |
| FR.06-Application-Shell | Window chrome | FR.6.1–6.4 | FR.6.5–6.8 | staged |
| FR.07-Code-Reference-Autocomplete | Code references | FR.7.1–7.12 | — | phase 2 |
| FR.08-Self-Update | Self-update on exit | FR.8.1–8.5 | — (see FR.32) | phase 2 |
| FR.09-Theme-and-Colours | Theme, colours, recency | FR.9.1–9.8 | FR.9.9–9.11 (FR.9.5 replaced) | staged |
| FR.10-Output-Search | Output search | FR.10.1–10.6 | — | phase 2 |
| FR.11-Session-Instruction-Toolbar | Instruction toolbar, profile adoption | FR.11.1–11.13 | FR.11.14 | staged |
| FR.12-Input-and-Composer | Input bar, model selection, composer | FR.12.1–12.29 | FR.12.30–12.34 | staged |
| FR.13-Session-Import | Session import | FR.13.1–13.15 | — | phase 2 |
| FR.14-Scheduled-Turns | Scheduled turns | FR.14.1–14.12 | FR.14.13 | staged |
| FR.15-Agent-Orchestration | Orchestration | FR.15.1–15.14 | — | phase 2 |
| FR.16-Model-Tier-Governance | Model tiers | FR.16.1–16.5 | — | phase 2 |
| FR.17-Token-Capture | Token capture | FR.17.1–17.3 | — | phase 2 |
| FR.18-Status-Bar | Plan usage and pricing | FR.18.1–18.8 | — (layout in FR.6.8) | phase 2 |
| FR.19-Streaming-Mode | Streaming mode | FR.19.1–19.13 | — | phase 2 |
| FR.20-External-MCP-Registry | MCP registry | FR.20.1–20.9 | FR.20.10 | staged (shared file `FR.20-21-25-Settings-Administration`, split in phase 2) |
| FR.21-Telegram-Bridge | Telegram | FR.21.1–21.24 | FR.21.25 | staged (same shared file) |
| FR.22-Completion-Sound | Sound | FR.22.1–22.5 | — | phase 2 |
| FR.23-Focus-View | Focus view | FR.23.1–23.19 | FR.23.20–23.23 | staged |
| FR.24-Session-Tags | Session tags | FR.24.1–24.15 | FR.24.16–24.18 | staged |
| FR.25-Web-Client | Tessyn Web (from the draft in the main tree) | FR.25.01–25.40 | FR.25.41 | phase 2 |
| FR.26-Links-and-Previews | Links and previews | FR.26.x | — | phase 2 |
| FR.31-Attachments | Attachments | FR.31.1–31.9 | — | phase 2 |
| **FR.32-Client-Hosting** | WPF hosting, background daemon, coexistence | — | FR.32.1–32.8 (new area) | staged |
| NR-Non-Functional | NR.1–NR.7 | NR.1–NR.5 | NR.6, NR.7 | staged |
| Out-of-Scope | Out of scope list | — | — | phase 2 |

FR.27–FR.30 stay **reserved** (embedded browser pane, output modes, multi-screen layout, helper models; `docs/refactoring/wpf-programme-plan.md`).

## Supporting staged text
- [tdcp-spec-additions.md](tdcp-spec-additions.md) — blocks for `docs/refactoring/tdcp-spec.md` and the glossary.
- Use cases and test cases of the batch: `UC/TC.LP.01`, `TR.01`, `IN.01`, `CL.01`, `SH.01`, `TA.01`, `FC.01`, `HS.01`.
