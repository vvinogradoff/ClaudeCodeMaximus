import { createContext, useCallback, useContext, useEffect, useMemo, useReducer, useRef } from 'react';
import type { ReactNode } from 'react';
import { EventStream } from '../api/events';
import { getTransport, RpcError, rpc, setUnauthorizedHandler } from '../api/rpc';
import type {
	AttentionAnswer, AttentionChangedParams, AttentionListResult, AttentionReplyResult, CatalogResult, CreateSessionResult,
	EventFrame, HelloResult, LinkStatusParams, SessionAppendedParams, SessionChangedParams, SessionDetailDto,
	SessionEntriesResult, SessionEntryDto, SessionSendResult, TreeSnapshotResult, TreeWorkersResult, TurnCompletedParams, TurnOptions,
} from '../api/types';
import { planAppended } from '../lib/transcript';
import { clearDrafts, readJson, removeKey, writeJson } from '../lib/storage';
import { initialState, reduce } from './appState';
import type { AppState, PanelTab, SessionTab, TurnSettings } from './appState';
import { publishEvent, requestComposerFocus } from './bus';
import { setExpanded } from './expansion';

const PAGE = 50;
const FETCH_LIMIT = 200;
const PENDING_DROP_MS = 30_000;
const DEBOUNCE_TREE_MS = 150;
const HASH_PREFIX = '#/s/';

export interface Actions {
	login(email: string, password: string): Promise<void>;
	logout(): Promise<void>;
	openSession(id: string): void;
	closeSession(): void;
	setPanel(open: boolean): void;
	setPanelTab(tab: PanelTab): void;
	refreshTree(): Promise<void>;
	loadWorkers(supervisorId: string): Promise<void>;
	resync(): Promise<void>;
	send(text: string): Promise<boolean>;
	stop(): Promise<void>;
	compact(): Promise<void>;
	loadEarlier(): Promise<void>;
	loadFullEntry(entry: SessionEntryDto): Promise<void>;
	setSessionTab(tab: SessionTab): void;
	refreshAttention(): Promise<void>;
	replyAttention(answers: AttentionAnswer[]): Promise<boolean>;
	dismissAttention(itemId: string): Promise<void>;
	setFavorite(value: boolean): Promise<void>;
	setStreaming(enabled: boolean, force?: boolean): Promise<boolean>;
	setTurnSettings(patch: Partial<TurnSettings>): void;
	createSession(anchorId: string, name: string): Promise<boolean>;
	hideRecent(id: string): Promise<void>;
	toast(text: string, kind?: 'error' | 'info'): void;
	dispatch: React.Dispatch<import('./appState').Action>;
}

interface Ctx { state: AppState; actions: Actions }
const AppCtx = createContext<Ctx | null>(null);

export function useApp(): Ctx {
	const c = useContext(AppCtx);
	if (!c) throw new Error('AppProvider missing');
	return c;
}

function hashSessionId(): string | null {
	const h = window.location.hash;
	return h.startsWith(HASH_PREFIX) ? decodeURIComponent(h.slice(HASH_PREFIX.length)) || null : null;
}

let toastSeq = 1;
let pendingSeq = 1;

export function AppProvider({ children }: { children: ReactNode }) {
	const [state, dispatch] = useReducer(reduce, undefined, () => initialState(readJson<PanelTab>('panelTab', 'tree')));
	const stateRef = useRef(state);
	stateRef.current = state;
	const streamRef = useRef<EventStream | null>(null);
	const treeTimer = useRef<number | undefined>(undefined);
	const fetchingAfter = useRef<{ running: boolean; dirty: boolean }>({ running: false, dirty: false });

	const toast = useCallback((text: string, kind: 'error' | 'info' = 'error') => {
		const id = toastSeq++;
		dispatch({ type: 'toast', toast: { id, text, kind } });
		window.setTimeout(() => dispatch({ type: 'toastDismiss', id }), 5000);
	}, []);

	const fail = useCallback((e: unknown) => {
		if (e instanceof RpcError && e.code === 401) return;
		toast(e instanceof Error ? e.message : String(e));
	}, [toast]);

	const openId = () => stateRef.current.session?.id ?? null;

	// ---- loading ---------------------------------------------------------------------------------------------
	const refreshTree = useCallback(async () => {
		try {
			const r = await rpc<TreeSnapshotResult>('tree.snapshot', {});
			if (r.directories) dispatch({ type: 'tree', directories: r.directories });
		} catch (e) { fail(e); }
	}, [fail]);

	const loadWorkers = useCallback(async (supervisorId: string) => {
		try {
			const r = await rpc<TreeWorkersResult>('tree.workers', { supervisorId });
			dispatch({ type: 'workers', supervisorId, workers: r.workers });
		} catch (e) { fail(e); }
	}, [fail]);

	const fetchAttention = useCallback(async (nodeId: string) => {
		try {
			const r = await rpc<AttentionListResult>('attention.list', { nodeId });
			dispatch({ type: 'attention', result: r });
		} catch (e) { fail(e); }
	}, [fail]);

	const reloadEntries = useCallback(async (nodeId: string) => {
		const r = await rpc<SessionEntriesResult>('session.entries', { nodeId, limit: PAGE });
		dispatch({ type: 'transcriptReplace', result: r });
	}, []);

	const fetchAfter = useCallback(async (nodeId: string) => {
		const flag = fetchingAfter.current;
		if (flag.running) { flag.dirty = true; return; }
		flag.running = true;
		try {
			do {
				flag.dirty = false;
				if (openId() !== nodeId) return;
				const t = stateRef.current.session!.transcript;
				if (!t.loaded || t.entries.length === 0) { await reloadEntries(nodeId); continue; }
				const afterIndex = t.entries[t.entries.length - 1].index;
				const r = await rpc<SessionEntriesResult>('session.entries', { nodeId, afterIndex, limit: FETCH_LIMIT });
				if (openId() !== nodeId) return;
				if (r.epoch !== t.epoch) { await reloadEntries(nodeId); continue; }
				dispatch({ type: 'transcriptAppend', result: r });
				if (r.entries.length >= FETCH_LIMIT) flag.dirty = true;
			} while (flag.dirty);
		} catch (e) { fail(e); } finally { flag.running = false; }
	}, [fail, reloadEntries]);

	const loadSession = useCallback(async (id: string) => {
		dispatch({ type: 'openStart', id });
		try {
			const [detail, entries] = await Promise.all([
				rpc<SessionDetailDto>('session.get', { nodeId: id }),
				rpc<SessionEntriesResult>('session.entries', { nodeId: id, limit: PAGE }),
			]);
			if (openId() !== id) return;
			dispatch({ type: 'detail', id, detail });
			dispatch({ type: 'transcriptReplace', result: entries });
		} catch (e) {
			if (openId() === id) dispatch({ type: 'openError', id, message: e instanceof Error ? e.message : String(e) });
			fail(e);
		}
	}, [fail]);

	const resync = useCallback(async () => {
		try {
			const hello = await rpc<HelloResult>('client.hello', {});
			dispatch({ type: 'serverTime', serverTimeUtc: hello.serverTimeUtc });
		} catch { /* ignore, offline */ }
		await refreshTree();
		const id = openId();
		if (id) {
			try {
				const detail = await rpc<SessionDetailDto>('session.get', { nodeId: id });
				if (openId() === id) {
					dispatch({ type: 'detail', id, detail });
					dispatch({ type: 'sessionChanged', state: detail.state });
					const t = stateRef.current.session?.transcript;
					if (!t || !t.loaded || t.epoch !== detail.epoch) await reloadEntries(id);
					else await fetchAfter(id);
					await fetchAttention(id);
				}
			} catch (e) { fail(e); }
		}
		dispatch({ type: 'bumpLists' });
		publishEvent({ method: 'resync' });
	}, [fail, fetchAfter, fetchAttention, refreshTree, reloadEntries]);

	// ---- events ----------------------------------------------------------------------------------------------
	const onEvent = useCallback((frame: EventFrame) => {
		const p = (frame.params ?? {}) as Record<string, unknown>;
		switch (frame.method) {
			case 'link.status': {
				const { online } = p as unknown as LinkStatusParams;
				const was = stateRef.current.online;
				dispatch({ type: 'online', online });
				if (online && !was) void resync();
				break;
			}
			case 'tree.changed':
				window.clearTimeout(treeTimer.current);
				treeTimer.current = window.setTimeout(() => void refreshTree(), DEBOUNCE_TREE_MS);
				break;
			case 'session.changed':
				dispatch({ type: 'sessionChanged', state: (p as unknown as SessionChangedParams).state });
				break;
			case 'session.appended':
			case 'session.reset': {
				const { nodeId, epoch } = p as unknown as SessionAppendedParams;
				const s = stateRef.current.session;
				const plan = s ? planAppended(s.transcript, s.id, nodeId, epoch) : 'ignore';
				if (frame.method === 'session.reset' && plan !== 'ignore') void reloadEntries(nodeId).catch(fail);
				else if (plan === 'reload') void reloadEntries(nodeId).catch(fail);
				else if (plan === 'fetchAfter') void fetchAfter(nodeId);
				break;
			}
			case 'turn.completed': {
				const { nodeId, error } = p as unknown as TurnCompletedParams;
				if (openId() === nodeId) {
					void fetchAfter(nodeId);
					if (error) toast(error);
					const now = Date.now();
					for (const m of stateRef.current.session?.pending ?? []) {
						if (!m.queued && now - m.createdAt > PENDING_DROP_MS) dispatch({ type: 'pendingRemove', nodeId, id: m.id });
					}
				}
				break;
			}
			case 'attention.changed': {
				const { nodeId, summary } = p as unknown as AttentionChangedParams;
				dispatch({ type: 'attentionSummary', nodeId, summary });
				const s = stateRef.current.session;
				if (s && s.id === nodeId && s.tab === 'focus') void fetchAttention(nodeId);
				break;
			}
			case 'tags.changed': {
				const nodeId = p.nodeId as string | undefined;
				const id = openId();
				if (id && (!nodeId || nodeId === id)) {
					void rpc<SessionDetailDto>('session.get', { nodeId: id }).then((d) => {
						if (openId() === id) dispatch({ type: 'detail', id, detail: d });
					}).catch(() => {});
				}
				break;
			}
		}
		publishEvent(frame);
	}, [fail, fetchAfter, fetchAttention, refreshTree, reloadEntries, resync, toast]);

	// ---- lifecycle -------------------------------------------------------------------------------------------
	const syncFromHash = useCallback(() => {
		const id = hashSessionId();
		const cur = openId();
		if (id && id !== cur) void loadSession(id);
		else if (!id && cur) dispatch({ type: 'close' });
	}, [loadSession]);

	const startSession = useCallback(async () => {
		try {
			const hello = await rpc<HelloResult>('client.hello', {});
			dispatch({ type: 'serverTime', serverTimeUtc: hello.serverTimeUtc });
		} catch { /* ignore */ }
		void rpc<CatalogResult>('catalog.get', {}).then((c) => dispatch({ type: 'catalog', catalog: c })).catch(() => {});
		void refreshTree();
		streamRef.current?.stop();
		const stream = new EventStream({ onEvent, onResync: () => void resync() });
		streamRef.current = stream;
		stream.start();
		syncFromHash();
	}, [onEvent, refreshTree, resync, syncFromHash]);

	const stopSession = useCallback(() => {
		streamRef.current?.stop();
		streamRef.current = null;
	}, []);

	useEffect(() => {
		setUnauthorizedHandler(() => {
			stopSession();
			dispatch({ type: 'auth', auth: 'out' });
		});
		let cancelled = false;
		void getTransport().me().then((me) => {
			if (cancelled) return;
			if (me) {
				dispatch({ type: 'auth', auth: 'in', email: me.email });
				void startSession();
			} else dispatch({ type: 'auth', auth: 'out' });
		}).catch(() => { if (!cancelled) dispatch({ type: 'auth', auth: 'out' }); });
		const onHash = () => { if (stateRef.current.auth === 'in') syncFromHash(); };
		window.addEventListener('hashchange', onHash);
		return () => { cancelled = true; window.removeEventListener('hashchange', onHash); stopSession(); };
		// eslint-disable-next-line react-hooks/exhaustive-deps
	}, []);

	useEffect(() => { writeJson('panelTab', state.panelTab); }, [state.panelTab]);

	// ---- actions ---------------------------------------------------------------------------------------------
	const turnOpts = (): TurnOptions => {
		const t = stateRef.current.session?.settings;
		return t ? { modelId: t.modelId, effort: t.effort, profileId: t.profileId } : {};
	};

	const actions = useMemo<Actions>(() => ({
		dispatch,
		toast,
		async login(email, password) {
			const r = await getTransport().login(email, password);
			dispatch({ type: 'auth', auth: 'in', email: r.email });
			await startSession();
		},
		async logout() {
			try {
				await getTransport().logout();
			} catch (err) {
				// The server still considers us logged in: stay signed in so the user knows.
				toast(err instanceof Error ? err.message : 'Log out failed', 'error');
				return;
			}
			clearDrafts();
			stopSession();
			dispatch({ type: 'close' });
			dispatch({ type: 'auth', auth: 'out' });
			window.location.hash = '';
		},
		openSession(id) {
			if (hashSessionId() === id) {
				if (openId() !== id) void loadSession(id);
				else dispatch({ type: 'panel', open: false });
				return;
			}
			window.location.hash = HASH_PREFIX + encodeURIComponent(id);
		},
		closeSession() { window.location.hash = ''; },
		setPanel: (open) => dispatch({ type: 'panel', open }),
		setPanelTab: (tab) => dispatch({ type: 'panelTab', tab }),
		refreshTree,
		loadWorkers,
		resync,
		async send(text) {
			const s = stateRef.current.session;
			if (!s) return false;
			const last = s.transcript.entries.length > 0 ? s.transcript.entries[s.transcript.entries.length - 1].index : -1;
			const id = `p${pendingSeq++}`;
			dispatch({ type: 'pendingAdd', nodeId: s.id, msg: { id, text, queued: false, baseIndex: last, createdAt: Date.now() } });
			try {
				const r = await rpc<SessionSendResult>('session.send', { nodeId: s.id, text, ...turnOpts() });
				if (r.queued) dispatch({ type: 'pendingQueued', nodeId: s.id, id });
				return true;
			} catch (e) {
				dispatch({ type: 'pendingRemove', nodeId: s.id, id });
				fail(e);
				return false;
			}
		},
		async stop() {
			const id = openId();
			if (!id) return;
			try { await rpc('session.stop', { nodeId: id }); } catch (e) { fail(e); }
		},
		async compact() {
			const id = openId();
			if (!id) return;
			try {
				await rpc('session.compact', { nodeId: id, ...turnOpts() });
				toast('Compacting the session…', 'info');
			} catch (e) { fail(e); }
		},
		async loadEarlier() {
			const s = stateRef.current.session;
			if (!s || s.transcript.entries.length === 0) return;
			try {
				const r = await rpc<SessionEntriesResult>('session.entries', { nodeId: s.id, beforeIndex: s.transcript.entries[0].index, limit: PAGE });
				dispatch({ type: 'transcriptPrepend', result: r });
			} catch (e) { fail(e); }
		},
		async loadFullEntry(entry) {
			const s = stateRef.current.session;
			if (!s) return;
			try {
				const full = await rpc<SessionEntryDto>('session.entry', { nodeId: s.id, index: entry.index, epoch: s.transcript.epoch });
				dispatch({ type: 'entryFull', nodeId: s.id, entry: full });
			} catch (e) {
				if (e instanceof RpcError && e.message.includes('history changed')) void reloadEntries(s.id).catch(fail);
				fail(e);
			}
		},
		setSessionTab(tab) {
			dispatch({ type: 'sessionTab', tab });
			const id = openId();
			if (tab === 'focus' && id) void fetchAttention(id);
		},
		async refreshAttention() { const id = openId(); if (id) await fetchAttention(id); },
		async replyAttention(answers) {
			const id = openId();
			if (!id || answers.length === 0) return false;
			try {
				const r = await rpc<AttentionReplyResult>('attention.reply', { nodeId: id, answers, ...turnOpts() });
				for (const itemId of r.answeredIds) removeKey(`reply.${id}.${itemId}`);
				if (r.skippedIds.length > 0) toast(`${r.skippedIds.length} card(s) were already closed`, 'info');
				await fetchAttention(id);
				return true;
			} catch (e) { fail(e); return false; }
		},
		async dismissAttention(itemId) {
			const id = openId();
			if (!id) return;
			try {
				await rpc('attention.dismiss', { nodeId: id, itemId });
				removeKey(`reply.${id}.${itemId}`);
				await fetchAttention(id);
			} catch (e) { fail(e); }
		},
		async setFavorite(value) {
			const s = stateRef.current.session;
			if (!s?.detail) return;
			const before = s.detail.state;
			dispatch({ type: 'sessionChanged', state: { ...before, favorite: value } });
			try { await rpc('session.setFavorite', { nodeId: s.id, value }); }
			catch (e) { dispatch({ type: 'sessionChanged', state: before }); fail(e); }
		},
		async setStreaming(enabled, force) {
			const id = openId();
			if (!id) return false;
			try {
				const r = await rpc<{ enabled: boolean; needsConfirm: boolean }>('session.setStreaming', { nodeId: id, enabled, ...(force ? { force: true } : {}) });
				if (r.needsConfirm) return true;
				dispatch({ type: 'detailPatch', nodeId: id, patch: { isStreamingMode: r.enabled } });
				return false;
			} catch (e) { fail(e); return false; }
		},
		setTurnSettings(patch) {
			const id = openId();
			if (id) dispatch({ type: 'turnSettings', nodeId: id, patch });
		},
		async createSession(anchorId, name) {
			try {
				const r = await rpc<CreateSessionResult>('tree.createSession', { anchorId, name });
				setExpanded(r.directoryId, true);
				if (r.groupId) setExpanded(r.groupId, true);
				await refreshTree();
				requestComposerFocus();
				this.openSession(r.session.id);
				return true;
			} catch (e) { fail(e); return false; }
		},
		async hideRecent(id) {
			try {
				await rpc('recent.hide', { nodeId: id });
				dispatch({ type: 'bumpLists' });
			} catch (e) { fail(e); }
		},
	}), [fail, fetchAfter, fetchAttention, loadSession, loadWorkers, refreshTree, reloadEntries, resync, startSession, stopSession, toast]);

	return <AppCtx.Provider value={{ state, actions }}>{children}</AppCtx.Provider>;
}
