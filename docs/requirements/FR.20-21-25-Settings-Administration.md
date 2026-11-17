# FR.20 / FR.21 / FR.25 — settings administration through the Remote API (batch 2026-11-17)

> **Staging file, phase 1.** From report 443 (J3 part 2). One requirement each in the area it belongs to. New IDs: **FR.20.10**, **FR.21.25**, **FR.25.41** (the draft's FR.25 ends at FR.25.40), and **FR.12.34**. Wire: `tdcp-spec` additions §A5. All methods are loopback only.

**FR.20.10 — Registry through the Remote API.** The External MCP Server Registry is a daemon service shared by every client. A client lists, adds, edits, removes, enables, discovers and authorises servers through the Remote API (`mcp.*`) without ever reading a secret back. Secrets (header values, OAuth client secrets and tokens) are **write-only**: results carry `hasStaticHeaderValue`, `hasClientSecret`, `hasToken`. Changing a server's **origin** (URL or OAuth token endpoint) deletes its stored token and requires the secrets to be sent again.

**FR.21.25 — Telegram settings through the Remote API.** The bot token can be set and cleared (write-only), and the bridge status (running, last error) read, through the Remote API.

**FR.25.41 — Relay link settings through the Remote API.** The relay link (URL, link token, on/off) can be configured through the Remote API; the link token is write-only and must be sent again when the URL moves to another origin.

**FR.12.34 — Model catalogue refresh.** A client can ask the daemon to refresh the model catalogue (local Ollama models) (`models.refresh`).

**Import of Avalonia settings (decision A23 Q8).** The daemon serves the Avalonia client's own settings read-only (`settings.get` section `avalonia-ui`: theme, colours, font sizes, key bindings, screensaver) for a one-time import into another client (FR.32.4). `settings.update` refuses that section.
