# Local-Models_Agentic-Coding-Findings

*Empirically verified 2026-09-11 against a locally running Ollama 0.32.14 instance. Backs [FR.12.21/FR.12.22](/docs/requirements.md#fr1221--local-tool-profile).*

## Pass-through architecture

Tessyn does not talk to Ollama directly and does not implement its own agentic loop for local models. A session using a local model still spawns the same `claude` CLI subprocess as an Anthropic-routed session (FR.12.14); only the environment variables differ:

- `ANTHROPIC_BASE_URL = {OllamaBaseUrl}` (bare host — see the FR.12.14 bug fix in this same round of changes: no `/v1` suffix, since the Anthropic SDK inside the CLI appends `/v1/messages` itself)
- `ANTHROPIC_AUTH_TOKEN = ollama`, `ANTHROPIC_API_KEY = ""`
- No `CLAUDE_CONFIG_DIR` (profile auth), no HTTPS proxy

Because the CLI process itself is unchanged, every native tool (Read/Write/Edit/Bash/Glob/Grep/LS/WebFetch/etc.) and every MCP server the CLI would normally offer an Anthropic model is offered to the local model too, over the same `/v1/messages` request shape. Ollama is acting purely as a drop-in HTTP replacement for `api.anthropic.com` from the CLI's point of view.

## Empirical findings (2026-09-11)

1. **Ollama 0.32.14 correctly serves the Anthropic `/v1/messages` protocol**, including the `tools` array in the request body. This was verified directly, not assumed from documentation.
2. **Tool calling works even on a small model.** A 9B-parameter model returned a syntactically valid `tool_use` block when given the CLI's tool schema — the protocol/serving layer is not the bottleneck.
3. **The real constraint is tool-surface size and model quality, not protocol support.** Weaker, quantized, or "abliterated" (safety-stripped, often at a quality cost) local models get confused when handed the CLI's full ~30-tool native surface plus a merged MCP server set. Confusion manifests as **inventing tool names that don't exist** — e.g. calling a hallucinated `read_file` instead of the CLI's actual `Read` tool — rather than as a protocol-level failure. This causes malformed/rejected tool calls and stalled or looping turns.
4. **A curated, smaller tool surface is the effective fix.** Reducing the offered tools to a lean, coding-relevant core (Read/Write/Edit/Bash/Glob/Grep/LS) removes most of the surface area a weaker model can get wrong, without removing any capability actually needed for typical coding turns (git itself is reachable via `Bash`, so no dedicated git tool is needed).

This is the basis for FR.12.21's Local Tool Profile: rather than trying to make local models smarter about tool selection, shrink the menu.

## Recommended local models for agentic coding

Based on the above, models purpose-tuned for **code + tool-calling** noticeably outperform general-purpose or abliterated variants at tool discipline, independent of raw parameter count. The following are ranked for a ~32 GB-VRAM / 64 GB unified-memory machine (e.g. an Asus Z13 / Ryzen AI Max+ "Strix Halo"). Benchmark/spec figures below are from published sources, not local testing here:

- **`qwen3.6:35b-a3b`** — *top pick for this hardware class.* 35B-total / **3B-active** sparse-MoE (hybrid Gated-DeltaNet linear attention + MoE), so it runs at roughly 3B-model speed while carrying 35B of knowledge; **262K** native context; native `tools`/`tool_choice` function calling with reasoning preserved across tool calls. Strong agentic-coding scores: **SWE-bench Verified 73.4**, SWE-bench Multilingual 67.2, Pro 49.5, **Terminal-Bench 2.0 51.5** (Terminal-Bench maps most directly to Claude-Code-style tool loops). Fits at **Q4_K_M (~24 GB)** inside a 32 GB VRAM budget; on unified memory, KV cache overflow spills to fast system RAM rather than choking, so mid/long contexts stay usable (full 262K would want ~48 GB or a smaller quant). Released 2026-04-15, Apache 2.0. Ollama gotcha: MoE models can under-utilise the GPU with default settings — ensure all layers offload and set a sane `num_ctx`.
- **`devstral`** — purpose-built for agentic coding; strong tool-call discipline; a lighter (24B dense) fallback when VRAM/context headroom is tighter.
- **`qwen3-coder:30b`** — coder-tuned Qwen3 variant; good balance of capability and reliability for local hardware.
- **`qwen2.5-coder:32b`** — coder-tuned Qwen2.5 variant (dense); mature and widely used, but slower than the A3B MoE above at similar quality.

**Coder-tuned beats general-purpose or abliterated for tool discipline.** A general-purpose chat model (even a larger one) or an abliterated variant of any model is more likely to mis-call tools than a smaller coder-tuned model, because tool-call formatting discipline is part of what the coder-tuning process specifically reinforces. Abliteration in particular strips safety training in a way that appears to also degrade instruction-following precision relevant to structured tool calls.

## Implication for the Local Tool Profile default

The FR.12.21 default lean profile (`Read`/`Write`/`Edit`/`Bash`/`Glob`/`Grep`/`LS`, no MCP) is deliberately conservative — it targets reliability on weaker/quantized coder models, not maximum capability. Users running a strong, well-behaved coder model locally can widen the profile (e.g. add `WebFetch`, enable the `cmx` MCP server) via the per-directory selector without affecting directories using weaker models.
