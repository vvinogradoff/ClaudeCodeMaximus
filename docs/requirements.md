# TessynDesktop — Requirements

## Project Overview

**TessynDesktop** is a Windows desktop application (WPF client over a UI-free daemon; see NR.1) that wraps the Claude Code CLI tool, providing a structured session management UI. It does not replace Claude Code — it hosts it, stores its history, and presents it in an organized, navigable interface.

---

## Anthropic T&C Compliance Note

This application is a **local desktop tool** that launches the `claude` CLI process already installed on the user's machine. It does not:
- Redistribute or bundle the Claude Code binary
- Bypass authentication or licensing
- Interact with the Anthropic API directly
- Claim affiliation with or endorsement by Anthropic

This is equivalent to writing a custom terminal emulator or IDE plugin that runs `claude` as a subprocess — a well-established and accepted category of tooling. No T&C violations are anticipated for personal use. **If the application is ever distributed publicly**, review Anthropic's usage policies and avoid implying official affiliation in branding.

---

## Functional Requirements

The requirements are split into one file per area in **[docs/requirements/](requirements/README.md)**. Start with the [index](requirements/README.md); the table [ID-MAP](requirements/ID-MAP.md) says where each requirement ID lives. IDs are never renumbered, so an ID quoted anywhere in `docs/` or in the code is found in the file of its area (`FR.<N>` is in `requirements/FR.<NN>-*.md`; `NR.<N>` in `requirements/NR-Non-Functional.md`).

## Glossary Reference

See `/docs/glossary.md` for domain term definitions.
