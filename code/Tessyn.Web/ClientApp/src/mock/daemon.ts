// Mock daemon: implements every contract method and event with realistic sample data. Dev/test only (VITE_MOCK=1).
import { LoginError, RpcError } from '../api/rpc';
import type { EventSourceHandle, EventSourceHandlers, LoginResult, Transport } from '../api/rpc';
import type {
	AttentionItemDto, AttentionKind, AttentionSummaryDto, EventFrame, SessionEntryDto, SessionStateDto, SessionTagDto, TagKind, TagRefDto,
	TreeDirectoryDto, TreeGroupDto, TreeSessionDto, UsageResult, EffortId,
} from '../api/types';
import { ago, LONG_ENTRY_FULL, LONG_ENTRY_PREVIEW, LONG_MARKDOWN, makeAssistantEntry, makeUserEntry, SHORT_REPLIES, USER_PROMPTS, WIDE_TABLE } from './samples';

const WORK_PROFILE = 'Work (vitaly@datalabs.example)';
const SYS_PROFILE = '[Sys] vitaly@example.com';
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
	lastUsed: { modelId: string; effort: EffortId; profileId: string; profileName: string };
	fullEntries: Record<number, string>;
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

		// --- a running session that appends entries every few seconds
		const live = put(tessyn, tessyn, undefined, { name: 'Live: nightly import monitor', lastActivityMin: 1, running: true, streaming: true }, 'monitor', 2);
		this.liveId = live.id;

		// --- supervisor with workers
		const sup = put(tessyn, tessyn, undefined, { name: 'Orchestrate WPF rewrite', lastActivityMin: 12, favorite: true, profileName: SYS_PROFILE }, 'wpf', 4);
		const workerNames = ['Worker: models', 'Worker: view-models', 'Worker: styles', 'Worker: tests'];
		workerNames.forEach((w, i) => {
			const rec = this.mk({ name: w, dirId: tessyn.id, groupId: undefined, supervisorId: sup.id, lastActivityMin: 20 + i * 9 });
			this.shortTranscript(rec, 2, w);
			tessyn.sessionIds.push(rec.id);
		});

		// --- attention items of every kind
		const att = put(tessyn, tessyn, undefined, { name: 'Needs your decision', lastActivityMin: 35, lastUsed: { modelId: 'default', effort: 'medium', profileId: 'default', profileName: 'Default' } }, 'attention', 2);
		this.addAttention(att, 'critical', 'Production database migration is blocked', 'The migration `0042_add_tags` failed on the staging replica.\n\nI have **stopped** before touching production.', [], undefined);
		this.addAttention(att, 'ask', 'Which retry policy should the relay use?', 'Two options are viable. Exponential backoff is the usual choice, but the link cap of 30 s is the main constraint.\n\n| Option | Max wait |\n|---|---|\n| Linear | 60 s |\n| Exponential | 30 s |', ['Exponential backoff', 'Linear backoff', 'Fixed 5 s'], 'Exponential backoff');
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
		this.addAttention(att2, 'ask', 'Drop the legacy column?', 'The column `Legacy_Id` has no readers left.', ['Drop it', 'Keep it'], 'Drop it');

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
	private summary(s: MockSession): AttentionSummaryDto {
		const order: AttentionKind[] = ['critical', 'ask', 'todo', 'alert', 'decided'];
		const open = s.attention.filter((a) => a.status === 'open');
		const top = (items: AttentionItemDto[]) => order.find((k) => items.some((i) => i.kind === k));
		const nonDecided = open.filter((a) => a.kind !== 'decided');
		return { openCount: nonDecided.length, topKind: top(nonDecided), allOpenCount: open.length, allOpenTopKind: top(open) };
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

	setOnline(online: boolean): void {
		this.online = online;
		this.emit('link.status', { online, since: new Date().toISOString() });
	}

	private touch(s: MockSession): void {
		s.lastActivityMin = 0;
		this.emit('session.changed', { state: this.stateOf(s) });
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
			case 'client.hello': return { protocolVersion: 1, daemonVersion: 'mock-1.0', machineName: 'MOCK-PC', runId: 'mock-run', serverTimeUtc: new Date().toISOString() };
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
			case 'session.send': return { accepted: true, queued: this.runTurn(this.get(params.nodeId), String(params.text ?? '')) };
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
					s.entries = [{ index: 0, timestamp: new Date().toISOString(), role: 'compaction', content: '' }, makeAssistantEntry(1, 0, '**Summary of the conversation so far**\n\n- Designed the web client\n- Agreed on SSE', 'claude-opus-5-5', 'high')];
					s.epoch++;
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
					{ id: 'default', displayName: 'Default', provider: 'anthropic' },
					{ id: 'claude-opus-5-5', displayName: 'Opus 5.5', provider: 'anthropic' },
					{ id: 'claude-sonnet-5-5', displayName: 'Sonnet 5.5', provider: 'anthropic' },
					{ id: 'claude-haiku-5-5', displayName: 'Haiku 5.5', provider: 'anthropic' },
				],
				efforts: ['default', 'max', 'high', 'medium', 'low'],
				profiles: [{ id: 'default', displayName: SYS_PROFILE }, { id: 'work', displayName: WORK_PROFILE }, { id: 'personal', displayName: 'Personal' }],
			};
			case 'usage.get': return this.usage(String(params.profileId ?? 'default'));
			default: throw new RpcError(-32601, `Method not found: ${method}`);
		}
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
		const answers = (params.answers as Array<{ itemId: string; text: string }>) ?? [];
		const answeredIds: string[] = [];
		const skippedIds: string[] = [];
		const lines: string[] = [];
		for (const a of answers) {
			const item = s.attention.find((x) => x.id === a.itemId);
			if (!item || item.status !== 'open') { skippedIds.push(a.itemId); continue; }
			item.status = 'answered';
			item.answer = a.text;
			item.closedUtc = new Date().toISOString();
			answeredIds.push(item.id);
			lines.push(`A${item.number} (${item.title}): ${a.text}`);
		}
		if (answeredIds.length > 0) {
			this.attentionChanged(s);
			this.runTurn(s, lines.join('\n'));
		}
		return { answeredIds, skippedIds, turnStarted: answeredIds.length > 0 };
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
		if (typeof params.afterIndex === 'number') slice = all.filter((e) => e.index > (params.afterIndex as number)).slice(0, limit);
		else if (typeof params.beforeIndex === 'number') slice = all.filter((e) => e.index < (params.beforeIndex as number)).slice(-limit);
		else slice = all.slice(-limit);
		return { nodeId: s.id, epoch: s.epoch, total: all.length, entries: slice };
	}

	private sessionEntry(params: Record<string, unknown>) {
		const s = this.get(params.nodeId);
		if (params.epoch !== s.epoch) throw new RpcError(-32000, 'Session history changed');
		const e = s.entries[Number(params.index)];
		if (!e) throw new RpcError(-32602, 'No such entry');
		const full = s.fullEntries[e.index];
		return full === undefined ? e : { ...e, content: full, truncated: undefined, fullLength: undefined };
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
		openEvents(handlers: EventSourceHandlers): EventSourceHandle {
			let closed = false;
			const off = daemon.subscribe((f) => { if (!closed) handlers.onFrame(f); });
			window.setTimeout(() => { if (!closed) handlers.onFrame({ method: 'link.status', params: { online: daemon.online, since: new Date().toISOString() } }); }, 0);
			return { close: () => { closed = true; off(); }, get closed() { return closed; } };
		},
	};
	return { transport, daemon };
}
