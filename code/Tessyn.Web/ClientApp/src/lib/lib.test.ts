import { describe, expect, it } from 'vitest';
import type { SessionDetailDto, SessionEntriesResult, SessionEntryDto, SessionStateDto, TreeDirectoryDto, TreeSessionDto } from '../api/types';
import { applySessionChanged, initialState, reduce } from '../state/appState';
import { effectiveExact, initialExactState, isAutoExact, toggleExact } from './exact';
import { reconcilePending } from './pending';
import type { PendingMsg } from './pending';
import { recencyBand } from './recency';
import { projectSearch } from './searchProjection';
import { appendEntries, emptyTranscript, planAppended, prependEntries, replaceTranscript, visibleEntries } from './transcript';

const st = (id: string, extra: Partial<SessionStateDto> = {}): SessionStateDto => ({ id, running: false, streamingAlive: false, favorite: false, hasDraft: false, ...extra });
const ts = (id: string, name = id, workerCount = 0): TreeSessionDto => ({ id, name, workerCount, state: st(id) });
const entry = (index: number, role: SessionEntryDto['role'], content: string): SessionEntryDto => ({ index, timestamp: '2026-01-01T00:00:00Z', role, content });
const result = (nodeId: string, epoch: number, entries: SessionEntryDto[], total = entries.length): SessionEntriesResult => ({ nodeId, epoch, total, entries });

describe('recency bands', () => {
	const now = Date.parse('2026-01-01T12:00:00Z');
	const at = (min: number) => new Date(now - min * 60000).toISOString();
	it('maps age to bands', () => {
		expect(recencyBand(at(5), now)).toBe('strong');
		expect(recencyBand(at(15), now)).toBe('strong');
		expect(recencyBand(at(16), now)).toBe('light');
		expect(recencyBand(at(60), now)).toBe('light');
		expect(recencyBand(at(200), now)).toBe('faint');
		expect(recencyBand(at(700), now)).toBe('fainter');
		expect(recencyBand(at(721), now)).toBe('none');
		expect(recencyBand(undefined, now)).toBe('none');
	});
});

describe('exact toggle rules', () => {
	it('auto-on for https:// and short queries', () => {
		expect(isAutoExact('abc')).toBe(true);
		expect(isAutoExact('https://x.example/a-long-url')).toBe(true);
		expect(isAutoExact('HTTPS://X')).toBe(true);
		expect(isAutoExact('a longer query')).toBe(false);
	});
	it('unticking holds until the text changes', () => {
		let s = initialExactState;
		expect(effectiveExact('abc', s)).toBe(true);
		s = toggleExact('abc', s, false);
		expect(effectiveExact('abc', s)).toBe(false);
		expect(effectiveExact('abcd', s)).toBe(true);
	});
	it('manual choice persists for non-auto queries', () => {
		let s = initialExactState;
		expect(effectiveExact('a longer query', s)).toBe(false);
		s = toggleExact('a longer query', s, true);
		expect(effectiveExact('another long query', s)).toBe(true);
		expect(effectiveExact('abc', s)).toBe(true);
		s = toggleExact('another long query', s, false);
		expect(effectiveExact('yet another query', s)).toBe(false);
	});
});

describe('tree search projection', () => {
	const tree: TreeDirectoryDto[] = [
		{
			id: 'd1', label: 'D1', isExpanded: false,
			groups: [
				{ id: 'g1', name: 'G1', isExpanded: false, groups: [], sessions: [ts('a'), ts('b')] },
				{ id: 'g2', name: 'G2', isExpanded: false, groups: [], sessions: [ts('c')] },
			],
			sessions: [ts('sup', 'sup', 2), ts('x')],
		},
		{ id: 'd2', label: 'D2', isExpanded: false, groups: [], sessions: [ts('y'), ts('z')] },
	];
	const m = (id: string, workerIds: string[] = []) => ({ id, strict: 1, loose: 1, matchedSelf: true, workerIds });

	it('shows only matches and ancestors', () => {
		const p = projectSearch(tree, {}, [m('c')]);
		expect(p.directories.map((d) => d.id)).toEqual(['d1']);
		const d1 = p.directories[0];
		expect(d1.children).toHaveLength(1);
		expect(d1.children[0]).toMatchObject({ kind: 'group', id: 'g2' });
	});
	it('orders directories, groups and sessions by rank (container = best descendant)', () => {
		const p = projectSearch(tree, {}, [m('y'), m('c'), m('x'), m('a')]);
		expect(p.directories.map((d) => d.id)).toEqual(['d2', 'd1']);
		const d1 = p.directories[1];
		expect(d1.children.map((c) => (c.kind === 'group' ? c.id : c.dto.id))).toEqual(['g2', 'x', 'g1']);
	});
	it('nests matching workers and reports unloaded ones', () => {
		const matches = [{ id: 'sup', strict: 0, loose: 2, matchedSelf: false, workerIds: ['w2'] }];
		const missing = projectSearch(tree, {}, matches);
		expect(missing.needWorkers).toEqual(['sup']);
		const loaded = projectSearch(tree, { sup: [ts('w1'), ts('w2')] }, matches);
		const sup = loaded.directories[0].children[0];
		expect(sup.kind === 'session' && sup.workers.map((w) => w.id)).toEqual(['w2']);
		expect(loaded.needWorkers).toEqual([]);
	});
	it('ignores matches that are not in the tree', () => {
		expect(projectSearch(tree, {}, [m('nope')]).directories).toEqual([]);
	});
});

describe('transcript merging and the appended plan', () => {
	const base = replaceTranscript(result('n', 0, [entry(5, 'user', 'a'), entry(6, 'assistant', 'b')], 10));
	it('replace marks earlier entries', () => {
		expect(base.hasEarlier).toBe(true);
		expect(replaceTranscript(result('n', 0, [entry(0, 'user', 'a')])).hasEarlier).toBe(false);
	});
	it('append adds only newer entries and ignores another epoch', () => {
		const t = appendEntries(base, result('n', 0, [entry(6, 'assistant', 'b'), entry(7, 'user', 'c')], 11));
		expect(t.entries.map((e) => e.index)).toEqual([5, 6, 7]);
		expect(appendEntries(base, result('n', 1, [entry(7, 'user', 'c')]))).toBe(base);
	});
	it('prepend adds older entries', () => {
		const t = prependEntries(base, result('n', 0, [entry(3, 'user', 'x'), entry(4, 'assistant', 'y')]));
		expect(t.entries.map((e) => e.index)).toEqual([3, 4, 5, 6]);
		expect(t.hasEarlier).toBe(true);
	});
	it('plans session.appended / session.reset handling', () => {
		expect(planAppended(base, 'n', 'other', 0)).toBe('ignore');
		expect(planAppended(base, null, 'n', 0)).toBe('ignore');
		expect(planAppended(base, 'n', 'n', 0)).toBe('fetchAfter');
		expect(planAppended(base, 'n', 'n', 1)).toBe('reload');
		expect(planAppended(emptyTranscript, 'n', 'n', 0)).toBe('reload');
	});
	it('hides whitespace-only entries except compaction', () => {
		const v = visibleEntries([entry(0, 'user', 'hi'), entry(1, 'assistant', '  \n'), entry(2, 'compaction', ''), entry(3, 'system', '')]);
		expect(v.map((e) => e.index)).toEqual([0, 2]);
	});
});

describe('event reducer: session.changed', () => {
	const detail = (running: boolean): SessionDetailDto => ({
		id: 'n', name: 'N', directoryId: 'd', location: { directoryLabel: 'D', treePath: 'N' }, state: st('n', { running }), tags: [],
		attention: { openCount: 0, allOpenCount: 0 }, lastUsed: { modelId: 'default', effort: 'default', profileId: 'default', profileName: 'Default' },
		isStreamingMode: false, entryCount: 0, epoch: 0,
	});
	it('overlays state and tracks runningSince for the open session', () => {
		let s = reduce(initialState('tree'), { type: 'openStart', id: 'n' });
		s = reduce(s, { type: 'detail', id: 'n', detail: detail(false) });
		expect(s.session?.runningSince).toBeNull();
		s = applySessionChanged(s, st('n', { running: true }), 1000);
		expect(s.states['n'].running).toBe(true);
		expect(s.session?.runningSince).toBe(1000);
		s = applySessionChanged(s, st('n', { running: true }), 5000);
		expect(s.session?.runningSince).toBe(1000);
		s = applySessionChanged(s, st('n', { running: false }), 6000);
		expect(s.session?.runningSince).toBeNull();
		expect(s.session?.detail?.state.running).toBe(false);
	});
	it('updates other sessions only in the overlay', () => {
		let s = reduce(initialState('tree'), { type: 'openStart', id: 'n' });
		s = applySessionChanged(s, st('other', { favorite: true }));
		expect(s.states['other'].favorite).toBe(true);
		expect(s.session?.id).toBe('n');
	});
	it('scope toggles default ON when a session is selected and OFF when none', () => {
		let s = initialState('tree');
		s = reduce(s, { type: 'select', selection: { id: 'd1', kind: 'directory' } });
		expect(s.scopeProject).toBe(false);
		s = reduce(s, { type: 'select', selection: { id: 'n', kind: 'session' } });
		expect(s.scopeProject).toBe(true);
		s = reduce(s, { type: 'scope', which: 'project', value: false });
		s = reduce(s, { type: 'select', selection: { id: 'n', kind: 'session' } });
		expect(s.scopeProject).toBe(false);
		s = reduce(s, { type: 'select', selection: { id: 'm', kind: 'session' } });
		expect(s.scopeProject).toBe(true);
		s = reduce(s, { type: 'select', selection: null });
		expect(s.scopeProject).toBe(false);
	});
});

describe('pending bubble reconciliation', () => {
	const p = (id: string, text: string, baseIndex = 3): PendingMsg => ({ id, text, queued: false, baseIndex, createdAt: 0 });
	it('removes a pending message once a matching USER entry arrives', () => {
		const out = reconcilePending([p('1', 'hello')], [entry(4, 'user', 'hello'), entry(5, 'assistant', 'hi')]);
		expect(out).toEqual([]);
	});
	it('keeps non-matching and older-than-base entries', () => {
		const pending = [p('1', 'hello')];
		expect(reconcilePending(pending, [entry(2, 'user', 'hello')])).toBe(pending);
		expect(reconcilePending(pending, [entry(4, 'user', 'other')])).toBe(pending);
		expect(reconcilePending(pending, [entry(4, 'assistant', 'hello')])).toBe(pending);
	});
	it('one entry fulfils one pending message', () => {
		const out = reconcilePending([p('1', 'same'), p('2', 'same')], [entry(4, 'user', 'same')]);
		expect(out.map((x) => x.id)).toEqual(['2']);
	});
	it('compares trimmed text with normalised newlines', () => {
		expect(reconcilePending([p('1', 'a\r\nb ')], [entry(4, 'user', ' a\nb')])).toEqual([]);
	});
});
