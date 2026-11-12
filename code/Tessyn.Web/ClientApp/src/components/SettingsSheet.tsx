import { useState } from 'react';
import { useApp } from '../state/AppContext';
import { ConfirmDialog } from './Dialogs';

const label = (s: string) => s.charAt(0).toUpperCase() + s.slice(1);

export function SettingsSheet({ onClose }: { onClose: () => void }) {
	const { state, actions } = useApp();
	const open = state.session!;
	const d = open.detail!;
	const settings = open.settings;
	const catalog = state.catalog;
	const locked = d.state.streamingAlive;
	const [confirmStop, setConfirmStop] = useState(false);

	const onStreaming = async (enabled: boolean) => {
		const needsConfirm = await actions.setStreaming(enabled);
		if (needsConfirm) setConfirmStop(true);
	};

	return (
		<div className="backdrop sheet-backdrop" onClick={onClose}>
			<div className="sheet" role="dialog" aria-label="Session settings" onClick={(e) => e.stopPropagation()} data-testid="settings-sheet">
				<div className="sheet-grab" />
				<h2>Session settings</h2>
				<label className="row">
					<span>★ Favourite</span>
					<input type="checkbox" className="switch" checked={d.state.favorite} onChange={(e) => void actions.setFavorite(e.target.checked)} />
				</label>
				<label className="row">
					<span>Model</span>
					<select value={settings?.modelId ?? 'default'} disabled={locked} onChange={(e) => actions.setTurnSettings({ modelId: e.target.value })}>
						{(catalog?.models ?? [{ id: 'default', displayName: 'Default' }]).map((m) => <option key={m.id} value={m.id}>{m.displayName}</option>)}
					</select>
				</label>
				<label className="row">
					<span>Profile</span>
					<select value={settings?.profileId ?? 'default'} disabled={locked} onChange={(e) => actions.setTurnSettings({ profileId: e.target.value })}>
						{(catalog?.profiles ?? [{ id: 'default', displayName: 'Default' }]).map((p) => <option key={p.id} value={p.id}>{p.displayName}</option>)}
					</select>
				</label>
				<label className="row">
					<span>Effort</span>
					<select value={settings?.effort ?? 'default'} disabled={locked} onChange={(e) => actions.setTurnSettings({ effort: e.target.value })}>
						{(catalog?.efforts ?? ['default', 'max', 'high', 'medium', 'low']).map((x) => <option key={x} value={x}>{label(x)}</option>)}
					</select>
				</label>
				{locked && <p className="hint">Model, profile and effort are locked while the streaming process is running.</p>}
				<label className="row">
					<span>Streaming</span>
					<input type="checkbox" className="switch" checked={d.isStreamingMode} onChange={(e) => void onStreaming(e.target.checked)} />
				</label>
				<button type="button" className="btn primary full" onClick={onClose}>Done</button>
			</div>
			{confirmStop && (
				<ConfirmDialog title="Turn streaming off" message="Stop the running Claude process for this session?" confirmLabel="Stop it"
					onCancel={() => setConfirmStop(false)}
					onConfirm={() => { setConfirmStop(false); void actions.setStreaming(false, true); }} />
			)}
		</div>
	);
}
