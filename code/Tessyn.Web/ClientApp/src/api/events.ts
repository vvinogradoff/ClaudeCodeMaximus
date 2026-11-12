import { getTransport } from './rpc';
import type { EventSourceHandle } from './rpc';
import type { EventFrame } from './types';

export const SILENT_REOPEN_MS = 30_000;

export interface EventStreamHandlers {
	/** Every dispatched frame (including link.status and resync). */
	onEvent(frame: EventFrame): void;
	/** The stream must be considered gapped: refetch everything. */
	onResync(): void;
	/** Connection-level error (the browser keeps retrying on its own). */
	onError?(): void;
}

/**
 * Wrapper around the SSE stream. Browser retries are automatic (Last-Event-ID is kept by EventSource);
 * on becoming visible again, a closed or silent (> 30 s) stream is reopened and the app resynced.
 */
export class EventStream {
	private handle: EventSourceHandle | null = null;
	private lastFrameAt = Date.now();
	private readonly onVisibility = () => {
		if (document.visibilityState !== 'visible' || !this.handle) return;
		const silent = Date.now() - this.lastFrameAt > SILENT_REOPEN_MS;
		if (this.handle.closed || silent) {
			this.open();
			this.handlers.onResync();
		}
	};

	constructor(private readonly handlers: EventStreamHandlers) {}

	start(): void {
		this.open();
		document.addEventListener('visibilitychange', this.onVisibility);
	}

	stop(): void {
		document.removeEventListener('visibilitychange', this.onVisibility);
		this.handle?.close();
		this.handle = null;
	}

	private open(): void {
		this.handle?.close();
		this.lastFrameAt = Date.now();
		this.handle = getTransport().openEvents({
			onFrame: (frame) => {
				this.lastFrameAt = Date.now();
				if (frame.method === 'resync') this.handlers.onResync();
				this.handlers.onEvent(frame);
			},
			onError: () => this.handlers.onError?.(),
		});
	}
}
