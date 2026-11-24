// Switching the profile of a session (ISSUE.013): pure helpers shared by the settings sheet, the session row and the tests.
import type { SessionDetailDto, SessionStateDto, SwitchProfileResult } from '../api/types';

const DEFAULT_PROFILE_ID = 'default';
const SYSTEM_PREFIX = '[Sys]';

/** The profile the session is on now: its pin (which follows a switch at once), else the last used one. */
export function currentProfileId(detail: Pick<SessionDetailDto, 'state' | 'lastUsed'>): string {
	return detail.state.pinnedProfileId ?? detail.lastUsed.profileId;
}

/** True when the picked profile is another one than the session is on. */
export function canSwitchTo(detail: Pick<SessionDetailDto, 'state' | 'lastUsed'>, pickedProfileId: string | undefined): boolean {
	return !!pickedProfileId && pickedProfileId !== currentProfileId(detail);
}

/**
 * The profile text of a session row. The last prompt's profile name is kept (the Default profile's `[Sys] email`) unless the pin moved on
 * since; a queued switch is appended.
 */
export function rowProfileLabel(state: Pick<SessionStateDto, 'profileName' | 'pinnedProfileId' | 'pinnedProfileName' | 'switchingToProfileName'>): string | undefined {
	const keepPrompt = !state.pinnedProfileName
		|| (state.pinnedProfileId === DEFAULT_PROFILE_ID && !!state.profileName?.startsWith(SYSTEM_PREFIX))
		|| state.pinnedProfileName === state.profileName;
	const base = keepPrompt ? state.profileName : state.pinnedProfileName;
	if (!state.switchingToProfileName) return base;
	return base ? `${base} → ${state.switchingToProfileName}` : `→ ${state.switchingToProfileName}`;
}

export interface SwitchSummary { text: string; problem: boolean }

/** The one-line outcome shown after a switch; `problem` when some session did not move completely. */
export function summarizeSwitch(result: SwitchProfileResult, sessionName: string): SwitchSummary {
	const parts: string[] = [];
	if (result.nodeMoved) parts.push(`"${sessionName}" moved to ${result.profileName}`);
	if (result.moved.length > 0) parts.push(`${result.moved.length} worker${result.moved.length === 1 ? '' : 's'} moved`);
	if (result.queued.length > 0) parts.push(`${result.queued.length} running, switched when ${result.queued.length === 1 ? 'its' : 'their'} turn ends`);
	if (result.failed.length > 0) {
		const files = result.failed.reduce((n, f) => n + f.fileCount, 0);
		parts.push(`${result.failed.length} could not be moved completely (${files} file${files === 1 ? '' : 's'}), retried at the next turn: ${result.failed.map((f) => f.name).join(', ')}`);
	}
	if (parts.length === 0) return { text: `"${sessionName}" is already on ${result.profileName}`, problem: false };
	return { text: parts.join('; '), problem: result.failed.length > 0 };
}
