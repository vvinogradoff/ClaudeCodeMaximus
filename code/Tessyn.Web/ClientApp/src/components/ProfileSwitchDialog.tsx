import { Dialog } from './Dialogs';

interface Props {
	sessionName: string;
	profileName: string;
	/** Direct workers of the session; 0 asks only for the confirmation. */
	workerCount: number;
	onChoose: (includeDescendants: boolean) => void;
	onCancel: () => void;
}

/** ISSUE.013, as the desktop asks: move the session alone, or with its workers too; a running session is switched when its turn ends. */
export function ProfileSwitchDialog({ sessionName, profileName, workerCount, onChoose, onCancel }: Props) {
	const note = 'Its transcript and files move to that profile. A running session is never interrupted: it is switched when its turn ends.';
	return (
		<Dialog title="Move session to profile" onClose={onCancel}>
			<p data-testid="profile-switch-message">
				{workerCount > 0
					? `"${sessionName}" has ${workerCount} worker${workerCount === 1 ? '' : 's'}. Move them to ${profileName} too? ${note}`
					: `Move "${sessionName}" to ${profileName}? ${note}`}
			</p>
			<div className="dialog-actions stacked">
				<button type="button" className="btn" onClick={onCancel}>Cancel</button>
				{workerCount > 0 && <button type="button" className="btn" onClick={() => onChoose(false)} data-testid="switch-only">This session only</button>}
				<button type="button" className="btn primary" onClick={() => onChoose(workerCount > 0)} data-testid="switch-confirm">
					{workerCount > 0 ? 'Session and workers' : 'Move'}
				</button>
			</div>
		</Dialog>
	);
}
