import { safeHttpsUrl } from '../lib/url';
import { useCallback, useEffect, useRef, useState } from 'react';
import { rpc } from '../api/rpc';
import type { TagChipDto, TagRefDto, TagSearchResult } from '../api/types';
import { useDebounced, useEventTrigger } from '../lib/hooks';
import { usePersistedState } from '../lib/storage';
import { effectiveState } from '../state/appState';
import { useApp } from '../state/AppContext';
import { SessionRow } from './SessionRow';

const TAG_DEBOUNCE_MS = 150;
const LONG_PRESS_MS = 550;

const CHIP_EMPTY: Record<string, string> = {
	noTagsScoped: 'No tags in this directory',
	noTagsUnscoped: 'No tags yet',
	noMatchingTags: 'No matching tags',
};
const SESSIONS_EMPTY: Record<string, string> = {
	selectTags: 'Select tags to list their sessions',
	noSessions: 'No sessions with the selected tags',
};

const KIND_ICON: Record<TagChipDto['kind'], string> = { linearIssue: '◆', gitHubPullRequest: '⇄', gitHubRepository: '❖' };

function Chip({ chip, onToggle }: { chip: TagChipDto; onToggle: () => void }) {
	const timer = useRef<number | undefined>(undefined);
	const fired = useRef(false);
	const href = safeHttpsUrl(chip.url);
	const open = () => { if (href) window.open(href, '_blank', 'noopener,noreferrer'); };
	return (
		<span className={`chip ${chip.selected ? 'sel' : ''}`}>
			<button type="button" className="chip-main" aria-pressed={chip.selected}
				onPointerDown={() => { fired.current = false; timer.current = window.setTimeout(() => { fired.current = true; open(); }, LONG_PRESS_MS); }}
				onPointerUp={() => window.clearTimeout(timer.current)} onPointerLeave={() => window.clearTimeout(timer.current)} onPointerCancel={() => window.clearTimeout(timer.current)}
				onContextMenu={(e) => e.preventDefault()}
				onClick={() => { if (!fired.current) onToggle(); }}>
				<span className="kind-ico">{KIND_ICON[chip.kind]}</span>{chip.label}<span className="count">{chip.sessionCount}</span>
			</button>
			{href && <a className="chip-link" href={href} target="_blank" rel="noopener noreferrer" aria-label={`Open ${chip.label}`}>↗</a>}
		</span>
	);
}

export function TagsTab({ active, nowMs }: { active: boolean; nowMs: number }) {
	const { state, actions } = useApp();
	const [text, setText] = useState('');
	const [selected, setSelected] = useState<TagRefDto[]>([]);
	const [data, setData] = useState<TagSearchResult | null>(null);
	const [collapsed, setCollapsed] = usePersistedState<string[]>('tagsCollapsed', []);
	const debounced = useDebounced(text.trim(), TAG_DEBOUNCE_MS);
	const sessionSelected = state.selected?.kind === 'session';
	const scopeNodeId = state.scopeTags && sessionSelected ? state.selected!.id : null;
	const [rev, setRev] = useState(0);

	useEventTrigger(['tags.changed'], () => setRev((r) => r + 1));

	const load = useCallback(() => {
		let cancelled = false;
		void rpc<TagSearchResult>('tags.search', { text: debounced, scopeNodeId, selected })
			.then((r) => { if (!cancelled) setData(r); }).catch(() => {});
		return () => { cancelled = true; };
	}, [debounced, scopeNodeId, selected]);

	useEffect(() => (active ? load() : undefined), [active, load, rev, state.listRevision]);

	const toggle = (c: TagChipDto) => {
		setSelected((cur) => (cur.some((t) => t.key === c.key) ? cur.filter((t) => t.key !== c.key) : [...cur, { key: c.key, label: c.label, url: c.url, kind: c.kind }]));
	};

	return (
		<div className={`tab ${active ? 'active' : ''}`} data-testid="tags-tab">
			<div className="search-bar">
				<div className="search-input">
					<input type="search" placeholder="Filter tags…" value={text} onChange={(e) => setText(e.target.value)} aria-label="Filter tags" />
					{text && <button type="button" className="clear" aria-label="Clear" onClick={() => setText('')}>×</button>}
				</div>
				<div className="toggles">
					<label className={`toggle ${sessionSelected ? '' : 'disabled'}`} title="Only tags of the selected session's directory">
						<input type="checkbox" disabled={!sessionSelected} checked={state.scopeTags && sessionSelected} onChange={(e) => actions.dispatch({ type: 'scope', which: 'tags', value: e.target.checked })} /> 📁 Directory
					</label>
				</div>
			</div>
			<div className="list">
				<div className="chips">
					{data?.chips.map((c) => <Chip key={c.key} chip={c} onToggle={() => toggle(c)} />)}
					{data?.emptyReason && <div className="empty">{CHIP_EMPTY[data.emptyReason]}</div>}
				</div>
				{data?.sessionsEmptyReason && <div className="empty">{SESSIONS_EMPTY[data.sessionsEmptyReason]}</div>}
				{data?.groups.map((g) => {
					const closed = collapsed.includes(g.directoryId);
					return (
						<div key={g.directoryId} className="dir">
							<button type="button" className="dir-row" aria-expanded={!closed}
								onClick={() => setCollapsed((c) => (closed ? c.filter((x) => x !== g.directoryId) : [...c, g.directoryId]))}>
								<span className="chev">{closed ? '›' : '⌄'}</span>📂 {g.directoryLabel} <span className="count">{g.sessions.length}</span>
							</button>
							{!closed && g.sessions.map((s) => (
								<SessionRow key={s.id} name={s.name} state={effectiveState(state, s.state)} nowMs={nowMs} selected={state.selected?.id === s.id} onClick={() => actions.openSession(s.id)} />
							))}
						</div>
					);
				})}
			</div>
		</div>
	);
}
