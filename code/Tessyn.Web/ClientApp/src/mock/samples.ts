// Sample transcript content for the mock daemon (never part of the production bundle).
import type { SessionEntryDto } from '../api/types';

export const LONG_MARKDOWN = `## Plan for the web client

I looked at how the desktop renders a transcript and propose the following structure:

1. **Data layer** - a typed JSON-RPC client plus an SSE event stream.
2. **State** - one reducer, no state library.
3. **Views** - sessions panel, session view, focus view, settings sheet.

### Key decisions

| Concern | Choice | Why |
|---|---|---|
| Rendering | React 19 | Concurrent features keep the transcript smooth on a phone |
| Markdown | \`marked\` + DOMPurify | Small, sanitised, no runtime eval |
| Routing | URL hash \`#/s/<id>\` | The iOS back gesture just works |
| Offline | Service worker for the shell only | Never cache \`/api/*\` |

\`\`\`ts
export async function rpc<T>(method: string, params: unknown = {}): Promise<T> {
	const res = await fetch('/api/rpc', {
		method: 'POST',
		credentials: 'same-origin',
		headers: { 'Content-Type': 'application/json', 'X-Tessyn-Client': 'web' },
		body: JSON.stringify({ jsonrpc: '2.0', id: nextId++, method, params }),
	});
	if (res.status === 401) throw new RpcError(401, 'Logged out');
	return (await res.json()).result as T;
}
\`\`\`

> Note: the transcript is append-only by index, so the client only ever asks for \`afterIndex\`.

- Streaming is a **session setting**, not a global one
- Draft text is kept per session in \`localStorage\`
- Links open in a new tab, e.g. [the docs](https://example.com/docs)

Let me know if you want the focus view to share the card component with the desktop layout.`;

export const SHORT_REPLIES = [
	'Done. I ran the **unit tests** and everything is green.\n\n```\nTest Run Successful.\nTotal tests: 1130\n     Passed: 1130\n```',
	'I found two places that still use the old name:\n\n- `docs/glossary.md`\n- `scripts/publish.ps1`\n\nShall I rename them?',
	'The failure is a *race* between the registry save and the tree refresh. A reference-counted running flag fixes it.',
	'Here is a quick comparison:\n\n| Option | Latency | Risk |\n|---|---|---|\n| Poll | 5 s | low |\n| SSE | <1 s | medium |\n| WebSocket | <1 s | high (proxy) |',
];

export const WIDE_TABLE = `Wide table to test horizontal scrolling:

| Method | Direction | Debounce | Producer | Consumer | Notes |
|---|---|---|---|---|---|
| tree.changed | daemon to browser | 300 ms | SessionRegistry | SPA tree | structural only |
| session.changed | daemon to browser | 250 ms per node | TurnController | SPA rows | running, attention, favourite |
| session.appended | daemon to browser | 250 ms per node | SessionFileService | SPA transcript | fetch afterIndex |`;

export const LONG_ENTRY_PREVIEW = 'This is a very long message that the server truncated at 32 KB. '.repeat(20);
export const LONG_ENTRY_FULL = LONG_ENTRY_PREVIEW + '\n\n' + 'And this is the remainder that only session.entry returns. '.repeat(40);

export function ago(minutes: number): string {
	return new Date(Date.now() - minutes * 60_000).toISOString();
}

export function makeUserEntry(index: number, minutesAgo: number, text: string, profile = 'Work (vitaly@datalabs.example)'): SessionEntryDto {
	return { index, timestamp: ago(minutesAgo), role: 'user', content: text, profileName: profile };
}

export function makeAssistantEntry(index: number, minutesAgo: number, text: string, modelId?: string, effort?: string): SessionEntryDto {
	return { index, timestamp: ago(minutesAgo), role: 'assistant', content: text, modelId, effort };
}

export const USER_PROMPTS = [
	'Please look at the failing test and tell me what is wrong.',
	'Can you add integration tests for the new endpoint?',
	'Summarise what changed in the last commit.',
	'Why does the tree refresh twice after creating a session?',
	'Rename the helper and update every caller.',
	'Check the docs and tell me whether the glossary needs a new term.',
];
