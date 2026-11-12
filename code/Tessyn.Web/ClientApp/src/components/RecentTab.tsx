import { useCallback, useEffect, useState } from 'react';
import { rpc } from '../api/rpc';
import type { RecentListResult } from '../api/types';
import { useEventTrigger } from '../lib/hooks';
import { effectiveState } from '../state/appState';
import { useApp } from '../state/AppContext';
import { SessionRow } from './SessionRow';

export function RecentTab({ active, nowMs }: { active: boolean; nowMs: number }) {
	const { state, actions } = useApp();
	const [data, setData] = useState<RecentListResult | null>(null);

	const load = useCallback(() => {
		void rpc<RecentListResult>('recent.list', {}).then(setData).catch(() => {});
	}, []);

	// Refetch when the tab is shown, after a resync/hide (listRevision), and (debounced) on session/attention changes.
	useEffect(() => { if (active) load(); }, [active, load, state.listRevision]);
	useEventTrigger(['session.changed', 'attention.changed'], () => { if (active) load(); });

	return (
		<div className={`tab ${active ? 'active' : ''}`} data-testid="recent-tab">
			<div className="list">
				{data && data.sessions.length === 0 && <div className="empty">No recent sessions</div>}
				{data?.sessions.map((r) => {
					const st = effectiveState(state, r.state);
					const trailing = r.canHide
						? <button type="button" className="hide-btn" aria-label="Hide from Recent" onClick={(e) => { e.stopPropagation(); void actions.hideRecent(r.id); }}>✕</button>
						: (st.favorite ? <span className="star">★</span> : null);
					return (
						<SessionRow key={r.id} name={r.name} state={st} nowMs={nowMs} label={r.directoryLabel} hasSchedules={r.hasSchedules}
							selected={state.selected?.id === r.id} trailing={trailing} onClick={() => actions.openSession(r.id)} />
					);
				})}
			</div>
		</div>
	);
}
