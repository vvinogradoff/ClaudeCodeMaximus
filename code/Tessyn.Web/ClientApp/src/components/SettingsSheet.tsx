import { useState } from 'react';
import { useApp } from '../state/AppContext';
import { codexStatusLines, DESKTOP_ONLY_NOTE, effectiveChoice, effortOptions, hasStreaming, modelGroups, modelTitle, profilesFor } from '../lib/engines';
import { ConfirmDialog } from './Dialogs';

export function SettingsSheet({ onClose }: { onClose: () => void }) {
	const { state, actions } = useApp();
	const open = state.session!;
	const d = open.detail!;
	const settings = open.settings;
	const catalog = state.catalog;
	const locked = d.state.streamingAlive;
	const [confirmStop, setConfirmStop] = useState(false);

	// FR.33: the pickers follow the model's engine; the stored choice is kept, so it returns when the model changes back.
	const choice = effectiveChoice(catalog, settings ?? { modelId: 'default', effort: 'default', profileId: 'default' }, d.state.engine);
	const { defaults, groups } = modelGroups(catalog);
	const efforts = effortOptions(catalog, choice.modelId);
	const profiles = profilesFor(catalog, choice.engine);
	const codex = codexStatusLines(state.codex);

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
					<select value={choice.modelId} disabled={locked} onChange={(e) => actions.setTurnSettings({ modelId: e.target.value })} data-testid="model-select">
						{defaults.map((m) => <option key={m.id} value={m.id}>{m.displayName}</option>)}
						{groups.map((g) => (
							<optgroup key={g.label} label={g.label}>
								{g.models.map((m) => <option key={m.id} value={m.id} title={modelTitle(m)}>{m.displayName}</option>)}
								{g.hint && <option disabled value="">{g.hint}</option>}
							</optgroup>
						))}
					</select>
				</label>
				<label className="row">
					<span>Profile</span>
					<select value={choice.profileId ?? ''} disabled={locked || profiles.length === 0} onChange={(e) => actions.setTurnSettings({ profileId: e.target.value })} data-testid="profile-select">
						{profiles.length === 0 && <option value="">No ChatGPT profile</option>}
						{profiles.map((p) => <option key={p.id} value={p.id}>{p.displayName}{p.plan ? ` (${p.plan})` : ''}</option>)}
					</select>
				</label>
				<label className="row">
					<span>Effort</span>
					<select value={choice.effort} disabled={locked} onChange={(e) => actions.setTurnSettings({ effort: e.target.value })} data-testid="effort-select">
						{efforts.map((x) => <option key={x.id} value={x.id} title={x.description}>{x.label}</option>)}
					</select>
				</label>
				{locked && <p className="hint">Model, profile and effort are locked while the streaming process is running.</p>}
				{hasStreaming(choice.engine) && (
					<label className="row">
						<span>Streaming</span>
						<input type="checkbox" className="switch" checked={d.isStreamingMode} onChange={(e) => void onStreaming(e.target.checked)} />
					</label>
				)}
				{codex && (
					<div className="row codex-status" data-testid="codex-status">
						<span>Codex</span>
						<span className="codex-status-text">
							<span>{codex.status}</span>
							{codex.note && <span className="hint">{codex.note}</span>}
						</span>
					</div>
				)}
				{codex && <p className="hint" data-testid="codex-desktop-note">{DESKTOP_ONLY_NOTE}</p>}
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
