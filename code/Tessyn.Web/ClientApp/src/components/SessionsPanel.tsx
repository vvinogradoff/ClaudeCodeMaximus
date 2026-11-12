import type { PanelTab } from '../state/appState';
import { useApp } from '../state/AppContext';
import { RecentTab } from './RecentTab';
import { TagsTab } from './TagsTab';
import { TreeTab } from './TreeTab';

const TABS: Array<{ id: PanelTab; label: string }> = [
	{ id: 'tree', label: 'Tree' },
	{ id: 'recent', label: 'Recent' },
	{ id: 'tags', label: 'Tags' },
];

/** Always mounted (hidden when closed) so search text, selected tags and scroll positions survive. */
export function SessionsPanel({ nowMs }: { nowMs: number }) {
	const { state, actions } = useApp();
	return (
		<section className={`panel ${state.panelOpen ? 'open' : ''}`} aria-hidden={!state.panelOpen} data-testid="sessions-panel">
			<div className="tabs" role="tablist">
				{TABS.map((t) => (
					<button key={t.id} type="button" role="tab" aria-selected={state.panelTab === t.id} className={state.panelTab === t.id ? 'on' : ''}
						onClick={() => actions.setPanelTab(t.id)}>{t.label}</button>
				))}
			</div>
			<div className="tab-body">
				<TreeTab active={state.panelOpen && state.panelTab === 'tree'} nowMs={nowMs} />
				<RecentTab active={state.panelOpen && state.panelTab === 'recent'} nowMs={nowMs} />
				<TagsTab active={state.panelOpen && state.panelTab === 'tags'} nowMs={nowMs} />
			</div>
		</section>
	);
}
