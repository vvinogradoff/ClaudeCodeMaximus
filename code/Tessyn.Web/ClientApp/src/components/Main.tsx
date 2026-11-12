import { useState } from 'react';
import { useTick } from '../App';
import { nowMs } from '../state/appState';
import { useApp } from '../state/AppContext';
import { ConfirmDialog } from './Dialogs';
import { SessionsPanel } from './SessionsPanel';
import { SessionView } from './SessionView';
import { SettingsSheet } from './SettingsSheet';
import { Toasts } from './Toasts';
import { TopBar } from './TopBar';
import { UsageStrip } from './UsageStrip';

export function Main() {
	const { state, actions } = useApp();
	useTick(60_000);
	const [settingsOpen, setSettingsOpen] = useState(false);
	const [confirmCompact, setConfirmCompact] = useState(false);
	const now = nowMs(state);

	return (
		<div className="app">
			<TopBar onSettings={() => setSettingsOpen(true)} onCompact={() => setConfirmCompact(true)} />
			<div className="body">
				<SessionView />
				<SessionsPanel nowMs={now} />
			</div>
			<UsageStrip />
			<Toasts />
			{settingsOpen && state.session?.detail && <SettingsSheet onClose={() => setSettingsOpen(false)} />}
			{confirmCompact && (
				<ConfirmDialog title="Compact session" message="Compact this session? The transcript is replaced by a summary." confirmLabel="Compact"
					onCancel={() => setConfirmCompact(false)} onConfirm={() => { setConfirmCompact(false); void actions.compact(); }} />
			)}
		</div>
	);
}
