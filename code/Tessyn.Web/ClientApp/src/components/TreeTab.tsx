import { useCallback, useEffect, useMemo, useState } from 'react';
import { rpc } from '../api/rpc';
import type { TreeGroupDto, TreeSearchResult, TreeSessionDto } from '../api/types';
import { effectiveExact, initialExactState, isAutoExact, toggleExact } from '../lib/exact';
import { useDebounced, useEventTrigger } from '../lib/hooks';
import { projectSearch } from '../lib/searchProjection';
import type { ProjChild } from '../lib/searchProjection';
import { effectiveState } from '../state/appState';
import { useApp } from '../state/AppContext';
import { isExpanded, toggleExpanded, useExpansionVersion } from '../state/expansion';
import { NameDialog } from './Dialogs';
import { SessionRow } from './SessionRow';

const SEARCH_DEBOUNCE_MS = 300;

export function TreeTab({ active, nowMs }: { active: boolean; nowMs: number }) {
	const { state, actions } = useApp();
	useExpansionVersion();
	const [text, setText] = useState('');
	const [exactState, setExactState] = useState(initialExactState);
	const [searching, setSearching] = useState(false);
	const [result, setResult] = useState<TreeSearchResult | null>(null);
	const [rev, setRev] = useState(0);
	const [creating, setCreating] = useState(false);
	const debounced = useDebounced(text, SEARCH_DEBOUNCE_MS);
	const query = debounced.trim();

	const sessionSelected = state.selected?.kind === 'session';
	const scopeNodeId = state.scopeProject && sessionSelected ? state.selected!.id : undefined;
	const exact = effectiveExact(text, exactState);

	useEventTrigger(['index.changed'], () => setRev((r) => r + 1), 0);

	// Run (or clear) the search whenever its inputs change.
	useEffect(() => {
		if (query === '') { setResult(null); setSearching(false); return; }
		let cancelled = false;
		setSearching(true);
		const exactForQuery = effectiveExact(query, exactState);
		void rpc<TreeSearchResult>('tree.search', { query, exact: exactForQuery, scopeNodeId: scopeNodeId ?? null })
			.then((r) => { if (!cancelled) { setResult(r); setSearching(false); } })
			.catch(() => { if (!cancelled) setSearching(false); });
		return () => { cancelled = true; };
	}, [query, exactState, scopeNodeId, rev, state.listRevision]);

	const projection = useMemo(
		() => (result && query !== '' ? projectSearch(state.tree, state.workers, result.matches) : null),
		[result, query, state.tree, state.workers],
	);
	const needKey = projection?.needWorkers.join(',') ?? '';
	useEffect(() => {
		if (!needKey) return;
		for (const id of needKey.split(',')) void actions.loadWorkers(id);
	}, [needKey, actions]);

	const openRow = useCallback((id: string) => actions.openSession(id), [actions]);

	const renderSession = (s: TreeSessionDto, indent: number, workers?: TreeSessionDto[]) => {
		const st = effectiveState(state, s.state);
		const supKey = `sup:${s.id}`;
		const expanded = workers ? true : isExpanded(supKey, false);
		const shownWorkers = workers ?? (expanded ? state.workers[s.id] ?? [] : []);
		const trailing = (
			<>
				{s.workerCount > 0 && !workers && (
					<button type="button" className="workers-btn" aria-label={`${s.workerCount} workers`} aria-expanded={expanded}
						onClick={(e) => { e.stopPropagation(); if (!expanded) void actions.loadWorkers(s.id); toggleExpanded(supKey, false); }}>
						{s.workerCount} workers {expanded ? '⌄' : '›'}
					</button>
				)}
				{st.favorite && <span className="star">★</span>}
			</>
		);
		return (
			<div key={s.id}>
				<SessionRow name={s.name} state={st} nowMs={nowMs} indent={indent} selected={state.selected?.id === s.id} trailing={trailing} onClick={() => openRow(s.id)} />
				{shownWorkers.map((w) => renderSession({ ...w, workerCount: 0 }, indent + 1))}
			</div>
		);
	};

	const renderGroup = (g: TreeGroupDto, depth: number) => {
		const open = isExpanded(g.id, g.isExpanded);
		return (
			<div key={g.id} className="group">
				<div className={`group-row ${state.selected?.id === g.id ? 'selected' : ''}`} style={{ marginLeft: depth * 12 }} role="button" tabIndex={0}
					onClick={() => { actions.dispatch({ type: 'select', selection: { id: g.id, kind: 'group' } }); toggleExpanded(g.id, g.isExpanded); }}>
					<span className="chev">{open ? '⌄' : '›'}</span>{g.name}
				</div>
				{open && (
					<>
						{g.groups.map((x) => renderGroup(x, depth + 1))}
						{g.sessions.map((s) => renderSession(s, depth + 1))}
					</>
				)}
			</div>
		);
	};

	const renderProj = (c: ProjChild, depth: number): React.ReactNode => {
		if (c.kind === 'session') return renderSession(c.dto, depth, c.workers);
		return (
			<div key={c.id} className="group">
				<div className="group-row" style={{ marginLeft: depth * 12 }}>{c.name}</div>
				{c.children.map((x) => renderProj(x, depth + 1))}
			</div>
		);
	};

	const placeholder = !state.treeLoaded ? 'Loading…' : '';
	const emptySearch = projection && projection.directories.length === 0;

	return (
		<div className={`tab ${active ? 'active' : ''}`} data-testid="tree-tab">
			<div className="search-bar">
				<div className="search-input">
					<input type="search" inputMode="search" enterKeyHint="search" placeholder="Search sessions…" value={text} onChange={(e) => setText(e.target.value)} aria-label="Search sessions" />
					{searching && <span className="spinner" aria-label="Searching" />}
					{text && !searching && <button type="button" className="clear" aria-label="Clear search" onClick={() => setText('')}>×</button>}
				</div>
				<div className="toggles">
					<label className="toggle"><input type="checkbox" checked={exact} onChange={(e) => setExactState((st) => toggleExact(text, st, e.target.checked))} /> Exact{isAutoExact(text) && text.trim() ? ' (auto)' : ''}</label>
					<label className={`toggle ${sessionSelected ? '' : 'disabled'}`}>
						<input type="checkbox" disabled={!sessionSelected} checked={state.scopeProject && sessionSelected} onChange={(e) => actions.dispatch({ type: 'scope', which: 'project', value: e.target.checked })} /> Project
					</label>
					<button type="button" className="btn small" disabled={!state.selected} onClick={() => setCreating(true)}>＋ Create session</button>
				</div>
			</div>
			<div className="list" data-testid="tree-list">
				{placeholder && <div className="empty">{placeholder}</div>}
				{projection ? (
					emptySearch ? <div className="empty">No matching sessions</div> : projection.directories.map((d) => (
						<div key={d.id} className="dir">
							<div className="dir-row">📂 {d.label}</div>
							{d.children.map((c) => renderProj(c, 0))}
						</div>
					))
				) : state.tree.map((d) => {
					const open = isExpanded(d.id, d.isExpanded);
					return (
						<div key={d.id} className="dir">
							<div className={`dir-row ${state.selected?.id === d.id ? 'selected' : ''}`} role="button" tabIndex={0}
								onClick={() => { actions.dispatch({ type: 'select', selection: { id: d.id, kind: 'directory' } }); toggleExpanded(d.id, d.isExpanded); }}>
								<span className="chev">{open ? '⌄' : '›'}</span>📂 {d.label}
							</div>
							{open && (
								<>
									{d.groups.map((g) => renderGroup(g, 0))}
									{d.sessions.map((s) => renderSession(s, 0))}
								</>
							)}
						</div>
					);
				})}
			</div>
			{creating && state.selected && (
				<NameDialog title="Create session" label="Session name" confirmLabel="Create" onCancel={() => setCreating(false)}
					onSubmit={(name) => { setCreating(false); void actions.createSession(state.selected!.id, name); }} />
			)}
		</div>
	);
}
