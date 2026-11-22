import type {
	AttentionListResult, AttentionSummaryDto, CatalogResult, CodexHelloStatusDto, HelloResult, SessionDetailDto, SessionEntriesResult, SessionEntryDto,
	SessionStateDto, TreeDirectoryDto, TreeSessionDto,
} from '../api/types';
import { reconcilePending } from '../lib/pending';
import type { PendingMsg } from '../lib/pending';
import { appendEntries, emptyTranscript, prependEntries, replaceEntry, replaceTranscript } from '../lib/transcript';
import type { TranscriptState } from '../lib/transcript';
import { popFrame, pushFrame } from '../lib/navStack';
import type { NavFrame } from '../lib/navStack';

export type PanelTab = 'tree' | 'recent' | 'tags';
export type SessionTab = 'session' | 'focus';
export type AuthState = 'unknown' | 'in' | 'out';
export interface Selection { id: string; kind: 'directory' | 'group' | 'session' }

/** Per-session client-side turn settings, sent with every send/reply/compact (initialised from session.get.lastUsed). */
export interface TurnSettings { modelId: string; effort: string; profileId: string }

/** A Focus ref the user jumped to: the transcript scrolls to the entry; `mark` also highlights offset/length. */
export interface FocusEntry { entryId: string; offset?: number; length?: number; mark: boolean; token: number }

/** What the file preview sheet shows (from a file ref or a file.preview handle). */
export interface PreviewTarget { handle: string; name: string; mime: string; size: number; title?: string }

export interface OpenSession {
	id: string;
	detail: SessionDetailDto | null;
	error: string | null;
	transcript: TranscriptState;
	pending: PendingMsg[];
	tab: SessionTab;
	attention: AttentionListResult | null;
	runningSince: number | null;
	settings: TurnSettings | null;
	focusEntry: FocusEntry | null;
}

export interface Toast { id: number; text: string; kind: 'error' | 'info' }

export interface AppState {
	auth: AuthState;
	email: string | null;
	online: boolean;
	serverOffsetMs: number;
	tree: TreeDirectoryDto[];
	treeLoaded: boolean;
	workers: Record<string, TreeSessionDto[]>;
	/** Latest known per-session state from events (overrides the DTO state in tree rows). */
	states: Record<string, SessionStateDto>;
	catalog: CatalogResult | null;
	/** The read-only Codex status of the last `client.hello` (FR.33.17); null from an older daemon or one without Codex. */
	codex: CodexHelloStatusDto | null;
	panelOpen: boolean;
	panelTab: PanelTab;
	selected: Selection | null;
	session: OpenSession | null;
	/** Jump history: Back returns to the previous place, also across sessions and for nested jumps. */
	navStack: NavFrame[];
	preview: PreviewTarget | null;
	toasts: Toast[];
	/** Bumped on resync / events that make list tabs refetch. */
	listRevision: number;
	/** Project (tree search) / folder (tags) scope toggles; default ON when a session becomes selected. */
	scopeProject: boolean;
	scopeTags: boolean;
}

export function initialState(panelTab: PanelTab): AppState {
	return {
		auth: 'unknown', email: null, online: true, serverOffsetMs: 0,
		tree: [], treeLoaded: false, workers: {}, states: {}, catalog: null, codex: null,
		panelOpen: false, panelTab, selected: null, session: null, navStack: [], preview: null, toasts: [], listRevision: 0, scopeProject: false, scopeTags: false,
	};
}

export type Action =
	| { type: 'auth'; auth: AuthState; email?: string | null }
	| { type: 'online'; online: boolean }
	| { type: 'serverTime'; serverTimeUtc: string }
	| { type: 'hello'; hello: HelloResult }
	| { type: 'tree'; directories: TreeDirectoryDto[] }
	| { type: 'workers'; supervisorId: string; workers: TreeSessionDto[] }
	| { type: 'catalog'; catalog: CatalogResult }
	| { type: 'sessionChanged'; state: SessionStateDto }
	| { type: 'panel'; open: boolean }
	| { type: 'panelTab'; tab: PanelTab }
	| { type: 'select'; selection: Selection | null }
	| { type: 'openStart'; id: string }
	| { type: 'close' }
	| { type: 'detail'; id: string; detail: SessionDetailDto }
	| { type: 'openError'; id: string; message: string }
	| { type: 'transcriptReplace'; result: SessionEntriesResult }
	| { type: 'transcriptAppend'; result: SessionEntriesResult }
	| { type: 'transcriptPrepend'; result: SessionEntriesResult }
	| { type: 'transcriptAround'; result: SessionEntriesResult; focus: FocusEntry | null }
	| { type: 'navPush'; frame: NavFrame }
	| { type: 'navPop' }
	| { type: 'navClear' }
	| { type: 'preview'; preview: PreviewTarget | null }
	| { type: 'entryFull'; nodeId: string; entry: SessionEntryDto }
	| { type: 'pendingAdd'; nodeId: string; msg: PendingMsg }
	| { type: 'pendingRemove'; nodeId: string; id: string }
	| { type: 'sessionTab'; tab: SessionTab }
	| { type: 'attention'; result: AttentionListResult }
	| { type: 'attentionSummary'; nodeId: string; summary: AttentionSummaryDto }
	| { type: 'turnSettings'; nodeId: string; patch: Partial<TurnSettings> }
	| { type: 'detailPatch'; nodeId: string; patch: Partial<SessionDetailDto> }
	| { type: 'toast'; toast: Toast }
	| { type: 'toastDismiss'; id: number }
	| { type: 'bumpLists' }
	| { type: 'scope'; which: 'project' | 'tags'; value: boolean }
	| { type: 'pendingQueued'; nodeId: string; id: string };

function freshSession(id: string): OpenSession {
	return { id, detail: null, error: null, transcript: emptyTranscript, pending: [], tab: 'session', attention: null, runningSince: null, settings: null, focusEntry: null };
}

function withSession(s: AppState, id: string, f: (o: OpenSession) => OpenSession): AppState {
	if (!s.session || s.session.id !== id) return s;
	return { ...s, session: f(s.session) };
}

/** Selection change: scope toggles default ON when a (different) session gets selected, OFF when none is. */
function withSelection(s: AppState, sel: Selection | null): AppState {
	const sessionSel = sel?.kind === 'session' ? sel.id : null;
	const prevSessionSel = s.selected?.kind === 'session' ? s.selected.id : null;
	if (sessionSel === null) return { ...s, selected: sel, scopeProject: false, scopeTags: false };
	if (sessionSel !== prevSessionSel) return { ...s, selected: sel, scopeProject: true, scopeTags: true };
	return { ...s, selected: sel };
}

function runningSinceFor(prev: number | null, running: boolean, now: number): number | null {
	if (!running) return null;
	return prev ?? now;
}

/** Applies `session.changed`: overlays the state and mirrors it into the open session. */
export function applySessionChanged(s: AppState, state: SessionStateDto, now: number = Date.now()): AppState {
	const next: AppState = { ...s, states: { ...s.states, [state.id]: state } };
	return withSession(next, state.id, (o) => ({
		...o,
		detail: o.detail ? { ...o.detail, state } : o.detail,
		runningSince: runningSinceFor(o.runningSince, state.running, now),
	}));
}

export function reduce(s: AppState, a: Action): AppState {
	switch (a.type) {
		case 'auth': return { ...s, auth: a.auth, email: a.email ?? null };
		case 'online': return { ...s, online: a.online };
		case 'serverTime': {
			const t = Date.parse(a.serverTimeUtc);
			return Number.isNaN(t) ? s : { ...s, serverOffsetMs: t - Date.now() };
		}
		case 'hello': {
			const t = Date.parse(a.hello.serverTimeUtc);
			return { ...s, codex: a.hello.codex ?? null, serverOffsetMs: Number.isNaN(t) ? s.serverOffsetMs : t - Date.now() };
		}
		case 'tree': return { ...s, tree: a.directories, treeLoaded: true, states: {} };
		case 'workers': return { ...s, workers: { ...s.workers, [a.supervisorId]: a.workers } };
		case 'catalog': return { ...s, catalog: a.catalog };
		case 'sessionChanged': return applySessionChanged(s, a.state);
		case 'panel': return { ...s, panelOpen: a.open };
		case 'panelTab': return { ...s, panelTab: a.tab };
		case 'select': return withSelection(s, a.selection);
		case 'openStart': return { ...withSelection(s, { id: a.id, kind: 'session' }), session: freshSession(a.id), panelOpen: false, preview: null };
		case 'close': return { ...s, session: null, navStack: [], preview: null };
		case 'detail': return withSession(s, a.id, (o) => ({
			...o,
			detail: a.detail,
			error: null,
			settings: o.settings ?? { modelId: a.detail.lastUsed.modelId, effort: a.detail.lastUsed.effort, profileId: a.detail.lastUsed.profileId },
			runningSince: runningSinceFor(o.runningSince, a.detail.state.running, Date.now()),
		}));
		case 'openError': return withSession(s, a.id, (o) => ({ ...o, error: a.message }));
		case 'transcriptReplace': return withSession(s, a.result.nodeId, (o) => {
			const t = replaceTranscript(a.result);
			return { ...o, transcript: t, pending: reconcilePending(o.pending, t.entries), focusEntry: null };
		});
		case 'transcriptAround': return withSession(s, a.result.nodeId, (o) => {
			const t = replaceTranscript(a.result, true);
			return { ...o, transcript: t, pending: reconcilePending(o.pending, t.entries), focusEntry: a.focus };
		});
		case 'navPush': return { ...s, navStack: pushFrame(s.navStack, a.frame) };
		case 'navPop': return { ...s, navStack: popFrame(s.navStack) };
		case 'navClear': return s.navStack.length === 0 ? s : { ...s, navStack: [] };
		case 'preview': return { ...s, preview: a.preview };
		case 'transcriptAppend': return withSession(s, a.result.nodeId, (o) => {
			const t = appendEntries(o.transcript, a.result);
			return { ...o, transcript: t, pending: reconcilePending(o.pending, t.entries) };
		});
		case 'transcriptPrepend': return withSession(s, a.result.nodeId, (o) => ({ ...o, transcript: prependEntries(o.transcript, a.result) }));
		case 'entryFull': return withSession(s, a.nodeId, (o) => ({ ...o, transcript: replaceEntry(o.transcript, a.entry) }));
		case 'pendingAdd': return withSession(s, a.nodeId, (o) => ({ ...o, pending: [...o.pending, a.msg] }));
		case 'pendingRemove': return withSession(s, a.nodeId, (o) => ({ ...o, pending: o.pending.filter((p) => p.id !== a.id) }));
		case 'sessionTab': return s.session ? { ...s, session: { ...s.session, tab: a.tab } } : s;
		case 'attention': return withSession(s, a.result.nodeId, (o) => ({
			...o, attention: a.result, detail: o.detail ? { ...o.detail, attention: a.result.summary } : o.detail,
		}));
		case 'attentionSummary': return withSession(s, a.nodeId, (o) => ({ ...o, detail: o.detail ? { ...o.detail, attention: a.summary } : o.detail }));
		case 'turnSettings': return withSession(s, a.nodeId, (o) => (o.settings ? { ...o, settings: { ...o.settings, ...a.patch } } : o));
		case 'detailPatch': return withSession(s, a.nodeId, (o) => ({ ...o, detail: o.detail ? { ...o.detail, ...a.patch } : o.detail }));
		case 'toast': return { ...s, toasts: [...s.toasts.slice(-3), a.toast] };
		case 'toastDismiss': return { ...s, toasts: s.toasts.filter((t) => t.id !== a.id) };
		case 'bumpLists': return { ...s, listRevision: s.listRevision + 1 };
		case 'scope': return a.which === 'project' ? { ...s, scopeProject: a.value } : { ...s, scopeTags: a.value };
		case 'pendingQueued': return withSession(s, a.nodeId, (o) => ({ ...o, pending: o.pending.map((p) => (p.id === a.id ? { ...p, queued: true } : p)) }));
	}
}

/** Effective state of a session row: the freshest of the event overlay and the DTO. */
export function effectiveState(s: AppState, dto: SessionStateDto): SessionStateDto {
	return s.states[dto.id] ?? dto;
}

export function nowMs(s: AppState): number {
	return Date.now() + s.serverOffsetMs;
}
