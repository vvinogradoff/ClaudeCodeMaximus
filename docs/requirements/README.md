# TessynDesktop — Requirements (index)

> **State.** The requirements were split from the single file into this folder on 2026-11-17 (reports `.wpf-work/486-docs.md`). `docs/requirements.md` stays as a short entry page (overview, Anthropic note, glossary reference, a pointer here) so old links still land. Every area file holds the **unchanged existing text** first and, after a rule, the *Amendments and additions (batch 2026-11-17)*.

## Rules
- IDs follow the `CLAUDE.md` scheme and are **never renumbered or reused**. [ID-MAP.md](ID-MAP.md) lists where every old section goes.
- One file per FR area: `FR.<NN>-<Slug>.md`. A section keeps its `### FR.<NN> — Title` heading and all its `FR.<NN>.<MM>` paragraphs.
- Amendments to an existing requirement are written as an **amendment table** at the top of the area file (existing ID, amended by, change) and as a **new ID** below, never as a silent edit. The old ID stays and points to the new one (FR.9.5 → FR.9.10).
- Use cases live in `docs/usecases/UC.<Code>.<NN>-*.md`, test cases in `docs/testcases/TC.<Code>.<NN>-*.md`, the same code in both.
- The wire protocol stays in `docs/refactoring/tdcp-spec.md`; a requirement names the field and points to the section.

## Index

| File | Area | Existing IDs | Batch 2026-11-17 adds |
|---|---|---|---|
| FR.01-Session-Tree | Session tree, left panel, Recent tab | FR.1.1–1.17 | FR.1.18–1.27 |
| FR.02-Session-View | Session view, transcript rows | FR.2.1–2.5, 2.3.x | FR.2.6–2.11 |
| FR.03-Session-Files | Session file format | FR.3.1–3.6 | — |
| FR.04-Application-Configuration | Application config | FR.4.1–4.6 | — |
| FR.05-Claude-Process-Management | Claude Code process | FR.5.1–5.5 | — |
| FR.06-Application-Shell | Window chrome | FR.6.1–6.4 | FR.6.5–6.8 |
| FR.07-Code-Reference-Autocomplete | Code references | FR.7.1–7.12 | — |
| FR.08-Self-Update | Self-update on exit | FR.8.1–8.5 | — (see FR.32) |
| FR.09-Theme-and-Colours | Theme, colours, recency | FR.9.1–9.8 | FR.9.9–9.11 (FR.9.5 replaced) |
| FR.10-Output-Search | Output search | FR.10.1–10.6 | — |
| FR.11-Session-Instruction-Toolbar | Instruction toolbar, profile adoption | FR.11.1–11.13 | FR.11.14 |
| FR.12-Input-and-Composer | Input bar, model selection, composer | FR.12.1–12.29 | FR.12.30–12.34 |
| FR.13-Session-Import | Session import | FR.13.1–13.15 | — |
| FR.14-Scheduled-Turns | Scheduled turns | FR.14.1–14.12 | FR.14.13 |
| FR.15-Agent-Orchestration | Orchestration | FR.15.1–15.14 | — |
| FR.16-Model-Tier-Governance | Model tiers | FR.16.1–16.5 | — |
| FR.17-Token-Capture | Token capture | FR.17.1–17.3 | — |
| FR.18-Status-Bar | Plan usage and pricing | FR.18.1–18.8 | — (layout in FR.6.8) |
| FR.19-Streaming-Mode | Streaming mode | FR.19.1–19.13 | — |
| FR.20-External-MCP-Registry | MCP registry | FR.20.1–20.9 | FR.20.10 |
| FR.21-Telegram-Bridge | Telegram | FR.21.1–21.24 | FR.21.25 |
| FR.22-Completion-Sound | Sound | FR.22.1–22.5 | — |
| FR.23-Focus-View | Focus view | FR.23.1–23.19 | FR.23.20–23.23 |
| FR.24-Session-Tags | Session tags | FR.24.1–24.15 | FR.24.16–24.18 |
| FR.25-Web-Client | Tessyn Web (from the draft in the main tree) | FR.25.01–25.40 | FR.25.41 |
| FR.26-Links-and-Previews | Links and previews | FR.26.x | — |
| FR.31-Attachments | Attachments | FR.31.1–31.9 | — |
| **FR.32-Client-Hosting** | WPF hosting, background daemon, coexistence | — | FR.32.1–32.8 (new area) |
| NR-Non-Functional | NR.1–NR.7 | NR.1–NR.5 | NR.6, NR.7 |
| Out-of-Scope | Out of scope list | — | — |

FR.27–FR.30 stay **reserved** (embedded browser pane, output modes, multi-screen layout, helper models; `docs/refactoring/wpf-programme-plan.md`).

## Supporting text
- The wire additions of the batch are in `docs/refactoring/tdcp-spec.md` section 10; the new glossary terms are in `docs/glossary.md`.
- Use cases and test cases of the batch: `UC/TC.LP.01`, `TR.01`, `IN.01`, `CL.01`, `SH.01`, `TA.01`, `FC.01`, `HS.01`.
