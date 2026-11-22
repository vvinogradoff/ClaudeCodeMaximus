import type { ElevatedRunDto } from '../api/types';
import { APPROVE_ON_PC, folderName, isFinalStatus, openStatusLine, outcomeLine, requestedBy } from '../lib/elevated';
import { formatTime } from '../lib/format';

// The elevated-run card of the Focus tab (FR.23.31, FR.23.32), READ-ONLY: the facts, the status, and "Approve on the PC" in place of the buttons.
// There is no Run, Decline or Stop here, in any state: approval happens only in the WPF client on the PC (the daemon refuses it through the relay).
// The command is shown whole and verbatim as plain text (never markdown, never parsed); the working directory is the folder name only.

interface FactsProps {
	run: ElevatedRunDto;
	sessionNodeId: string;
}

/** Who asked, the reason, the folder, the command and the limits. */
export function ElevatedFacts({ run, sessionNodeId }: FactsProps) {
	return (
		<div className="elev-facts" data-testid="elevated-facts">
			<div className="elev-by" data-testid="elevated-requested-by">{requestedBy(run, sessionNodeId)}</div>
			<div className="elev-label">Reason</div>
			<div className="elev-reason" data-testid="elevated-reason">{run.reason}</div>
			<div className="elev-label">Working directory</div>
			<div className="elev-dir" data-testid="elevated-dir">{folderName(run)}</div>
			<div className="elev-label">Command</div>
			<pre className="elev-command" data-testid="elevated-command" tabIndex={0} aria-label="Command">{run.command}</pre>
			<div className="elev-limit" data-testid="elevated-limit">Time limit: {run.timeoutMinutes} minutes</div>
			{run.status === 'pending' && <div className="elev-limit" data-testid="elevated-expires">Expires {formatTime(run.expiresUtc)}</div>}
		</div>
	);
}

interface CardProps extends FactsProps {
	now: number;
}

/** The state under the facts of an open request: the note while it waits for the PC, the status once it launches or runs, the outcome once it ended. */
export function ElevatedState({ run, now }: { run: ElevatedRunDto; now: number }) {
	if (run.status === 'pending') return <div className="elev-note" data-testid="elevated-approve-note">{APPROVE_ON_PC}</div>;
	if (isFinalStatus(run.status)) return <div className="elev-outcome" data-testid="elevated-outcome">{outcomeLine(run)}</div>;
	return <div className="elev-status" data-testid="elevated-status">{openStatusLine(run, now)}</div>;
}

/** The body of an open elevated-run card. */
export function ElevatedRunBody({ run, sessionNodeId, now }: CardProps) {
	return (
		<>
			<ElevatedFacts run={run} sessionNodeId={sessionNodeId} />
			<ElevatedState run={run} now={now} />
		</>
	);
}
