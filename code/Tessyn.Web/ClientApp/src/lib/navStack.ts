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
