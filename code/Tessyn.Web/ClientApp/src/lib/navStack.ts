// Back navigation for Focus-ref jumps: a stack of places the user left, so nested jumps unwind one step at a time.

export interface NavFrame {
	nodeId: string;
	/** Session name for the Back label. */
	title: string;
	tab: 'session' | 'focus';
	/** Entry at the top of the viewport when the user left (transcript tab, not at the bottom); undefined = restore the latest page. */
	anchorEntryId?: string;
}

export const NAV_MAX = 40;

export function pushFrame(stack: readonly NavFrame[], frame: NavFrame): NavFrame[] {
	const next = [...stack, frame];
	return next.length > NAV_MAX ? next.slice(next.length - NAV_MAX) : next;
}

export function popFrame(stack: readonly NavFrame[]): NavFrame[] {
	return stack.slice(0, -1);
}

export function topFrame(stack: readonly NavFrame[]): NavFrame | undefined {
	return stack[stack.length - 1];
}

/** "Back to <session> · Focus" / "Back to <session>". */
export function backLabel(frame: NavFrame): string {
	return `Back to ${frame.title}${frame.tab === 'focus' ? ' · Focus' : ''}`;
}

/** A one-shot "open this session at that entry" request, valid only for a short time and only for the session it names. */
export const NAV_TARGET_TTL_MS = 10_000;
export interface TimedTarget { nodeId: string; at: number }

/** The target when it is meant for the session that is opening now (and is fresh), else null. */
export function takeNavTarget<T extends TimedTarget>(target: T | null, openingId: string, now: number): T | null {
	return target && target.nodeId === openingId && now - target.at <= NAV_TARGET_TTL_MS ? target : null;
}

/** Whether a pending target still applies when the URL hash now names `hashId` (null = no session in the hash). */
export function keepNavTarget(target: TimedTarget | null, hashId: string | null, now: number): boolean {
	return !!target && hashId === target.nodeId && now - target.at <= NAV_TARGET_TTL_MS;
}
