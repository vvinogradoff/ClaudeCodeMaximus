import type { AttentionItemDto, ElevatedRunDto, ElevatedRunStatus } from '../api/types';

// The elevated-run card of the Focus tab (FR.23.31, FR.23.32): pure helpers. The web client shows the card read-only; the buttons live on the PC.

export const ELEVATED_ACTION = 'elevatedRun';

/** The note in place of the buttons while the request waits (FR.23.31). */
export const APPROVE_ON_PC = 'Approve on the PC';

/** The run an item carries, or undefined for every other item (a plain Ask). */
export function elevatedRunOf(item: AttentionItemDto): ElevatedRunDto | undefined {
	return item.action?.type === ELEVATED_ACTION && item.action.run ? item.action.run : undefined;
}

/** Pending, launching and running are open; every other status is final. */
export function isFinalStatus(status: ElevatedRunStatus): boolean {
	return status !== 'pending' && status !== 'launching' && status !== 'running';
}

/** `1h 02m 05s`, `3m 12s`, `7s`. */
export function formatRunDuration(ms: number): string {
	const total = Math.max(0, Math.floor(ms / 1000));
	const h = Math.floor(total / 3600);
	const m = Math.floor((total % 3600) / 60);
	const s = total % 60;
	const two = (n: number) => String(n).padStart(2, '0');
	if (h > 0) return `${h}h ${two(m)}m ${two(s)}s`;
	return m > 0 ? `${m}m ${two(s)}s` : `${s}s`;
}

function lastSegment(path: string): string {
	const trimmed = path.replace(/[\\/]+$/, '');
	const i = Math.max(trimmed.lastIndexOf('\\'), trimmed.lastIndexOf('/'));
	return i >= 0 ? trimmed.slice(i + 1) : trimmed;
}

/** The folder name only: the relay sends `workingDirName`; a full path (never sent over the relay) is cut to its last folder as a safeguard. */
export function folderName(run: ElevatedRunDto): string {
	return run.workingDirName ?? (run.workingDir ? lastSegment(run.workingDir) : '');
}

/** The outcome line of a finished run (the wording of FR.23.32); empty while the run is open. */
export function outcomeLine(run: ElevatedRunDto): string {
	switch (run.status) {
		case 'completed': {
			if (run.exitCode === undefined) return 'Completed';
			const started = run.startedUtc ? Date.parse(run.startedUtc) : NaN;
			const ended = run.endedUtc ? Date.parse(run.endedUtc) : NaN;
			return !Number.isNaN(started) && !Number.isNaN(ended) && ended >= started
				? `Completed: exit code ${run.exitCode}, ${formatRunDuration(ended - started)}`
				: `Completed: exit code ${run.exitCode}`;
		}
		case 'declined': return 'Declined';
		case 'declinedAtPrompt': return 'Declined at the Windows prompt';
		case 'failedToStart': return run.failure ? `Failed to start: ${run.failure}` : 'Failed to start';
		case 'timedOut': return `Timed out after ${run.timeoutMinutes} min`;
		case 'cancelled': return run.cancelledBy === 'agent' ? 'Cancelled (withdrawn by the agent)' : 'Cancelled (stopped by the user)';
		case 'expired': return 'Expired';
		default: return '';
	}
}

/** The status line of an open run. */
export function openStatusLine(run: ElevatedRunDto, nowMs: number): string {
	if (run.status === 'launching') return 'Starting on the PC...';
	if (run.status === 'running') {
		const started = run.startedUtc ? Date.parse(run.startedUtc) : NaN;
		const elapsed = Number.isNaN(started) ? '' : `, ${formatRunDuration(nowMs - started)}`;
		return `Running as administrator on the PC (limit ${run.timeoutMinutes} min${elapsed})`;
	}

	return '';
}

/** `Requested by <name> (<short id>)`; on the supervisor's card for a worker's request: `Requested by worker <name> (<short id>) of this session`. */
export function requestedBy(run: ElevatedRunDto, sessionNodeId: string): string {
	const who = `${run.requester.name} (${run.requester.shortId})`;
	return run.requester.nodeId.toLowerCase() === sessionNodeId.toLowerCase() ? `Requested by ${who}` : `Requested by worker ${who} of this session`;
}
