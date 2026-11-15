import type { AttachmentResult, EventFrame } from './types';

export class RpcError extends Error {
	readonly code: number;
	constructor(code: number, message: string) {
		super(message);
		this.name = 'RpcError';
		this.code = code;
	}
}

export interface LoginResult { email: string }
export class LoginError extends Error {
	readonly status: number;
	readonly retryAfterSeconds?: number;
	constructor(status: number, message: string, retryAfterSeconds?: number) {
		super(message);
		this.name = 'LoginError';
		this.status = status;
		this.retryAfterSeconds = retryAfterSeconds;
	}
}

/** Upload failure; `status` is the HTTP status (0 = network error, -1 = aborted by the user). */
export class UploadError extends Error {
	readonly status: number;
	constructor(status: number, message: string) {
		super(message);
		this.name = 'UploadError';
		this.status = status;
	}
}

export interface UploadHandle { promise: Promise<AttachmentResult>; abort(): void }

export interface EventSourceHandle { close(): void; readonly closed: boolean }
export interface EventSourceHandlers { onFrame(frame: EventFrame): void; onError(): void }

/** Everything the UI needs from the outside world; implemented by HTTP (production) or the mock daemon. */
export interface Transport {
	rpc(method: string, params: unknown): Promise<unknown>;
	login(email: string, password: string): Promise<LoginResult>;
	logout(): Promise<void>;
	me(): Promise<LoginResult | null>;
	/** POST /api/attachments (multipart: `file`, `nodeId`); `onProgress` gets 0..1. */
	upload(nodeId: string, file: File, onProgress: (fraction: number) => void): UploadHandle;
	openEvents(handlers: EventSourceHandlers): EventSourceHandle;
}

let transport: Transport | null = null;
let unauthorizedHandler: () => void = () => {};

export function setTransport(t: Transport): void { transport = t; }
export function getTransport(): Transport {
	if (!transport) throw new Error('Transport not initialised');
	return transport;
}
export function setUnauthorizedHandler(h: () => void): void { unauthorizedHandler = h; }
export function notifyUnauthorized(): void { unauthorizedHandler(); }

export async function rpc<T>(method: string, params: unknown = {}): Promise<T> {
	return (await getTransport().rpc(method, params)) as T;
}

let nextId = 1;
export function newRequestId(): number { return nextId++; }
