import type { EventFrame } from '../api/types';

// Tiny pub-sub so list components (Recent, Tags, usage strip, tree search) can react to daemon events
// without the controller knowing about them.

type Listener = (frame: EventFrame) => void;
const listeners = new Set<Listener>();

export function publishEvent(frame: EventFrame): void {
	for (const l of [...listeners]) l(frame);
}

export function subscribeEvents(fn: Listener): () => void {
	listeners.add(fn);
	return () => { listeners.delete(fn); };
}

/** Where the transcript viewport is: set by the mounted Transcript so a jump can remember the place the user leaves. */
export interface ViewportInfo { firstVisibleEntryId: string | null; atBottom: boolean }
let viewportProbe: (() => ViewportInfo) | null = null;
export function setViewportProbe(fn: (() => ViewportInfo) | null): void { viewportProbe = fn; }
export function probeViewport(): ViewportInfo | null { return viewportProbe ? viewportProbe() : null; }

let focusRequested = false;
/** Ask the composer to take focus as soon as it is ready (after creating a session). */
export function requestComposerFocus(): void { focusRequested = true; }
export function consumeComposerFocus(): boolean {
	const v = focusRequested;
	focusRequested = false;
	return v;
}
