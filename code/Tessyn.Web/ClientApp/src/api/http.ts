import { LoginError, newRequestId, notifyUnauthorized, RpcError } from './rpc';
import type { LoginResult, Transport, EventSourceHandlers, EventSourceHandle } from './rpc';
import type { EventFrame } from './types';

interface RpcEnvelope { result?: unknown; error?: { code: number; message: string } }

const CODE_HTTP = -32603;
const CODE_OFFLINE = -32001;

/** CSRF defence: the server rejects every state-changing /api request without this header. Use it for ALL mutating fetches. */
export const CLIENT_HEADERS = { 'X-Tessyn-Client': 'web' } as const;

export const httpTransport: Transport = {
	async rpc(method, params) {
		let res: Response;
		try {
			res = await fetch('/api/rpc', {
				method: 'POST',
				credentials: 'same-origin',
				headers: { 'Content-Type': 'application/json', ...CLIENT_HEADERS },
				body: JSON.stringify({ jsonrpc: '2.0', id: newRequestId(), method, params }),
			});
		} catch {
			throw new RpcError(CODE_OFFLINE, 'Network error - check your connection');
		}
		if (res.status === 401) {
			notifyUnauthorized();
			throw new RpcError(401, 'Logged out');
		}
		let body: RpcEnvelope | null = null;
		try { body = (await res.json()) as RpcEnvelope; } catch { /* not JSON */ }
		if (body?.error) throw new RpcError(body.error.code, body.error.message);
		if (!res.ok || !body) throw new RpcError(CODE_HTTP, `Server error (${res.status})`);
		return body.result;
	},

	async login(email, password): Promise<LoginResult> {
		const res = await fetch('/api/auth/login', {
			method: 'POST',
			credentials: 'same-origin',
			headers: { 'Content-Type': 'application/json', ...CLIENT_HEADERS },
			body: JSON.stringify({ email, password }),
		});
		if (res.ok) return (await res.json()) as LoginResult;
		if (res.status === 429) {
			const retry = Number(res.headers.get('Retry-After'));
			throw new LoginError(429, 'Too many attempts', Number.isFinite(retry) && retry > 0 ? retry : undefined);
		}
		let message = 'Login failed';
		try { message = ((await res.json()) as { error?: string }).error ?? message; } catch { /* ignore */ }
		throw new LoginError(res.status, message);
	},

	async logout() {
		let res: Response;
		try {
			res = await fetch('/api/auth/logout', { method: 'POST', credentials: 'same-origin', headers: { ...CLIENT_HEADERS } });
		} catch {
			throw new Error('Network error - could not log out');
		}
		if (!res.ok) throw new Error(`Log out failed (${res.status})`);
	},

	async me() {
		const res = await fetch('/api/auth/me', { credentials: 'same-origin' });
		if (res.status === 401) return null;
		if (!res.ok) throw new Error(`me failed (${res.status})`);
		return (await res.json()) as LoginResult;
	},

	openEvents(handlers: EventSourceHandlers): EventSourceHandle {
		const es = new EventSource('/api/events', { withCredentials: true });
		es.onmessage = (ev: MessageEvent<string>) => {
			try { handlers.onFrame(JSON.parse(ev.data) as EventFrame); } catch { /* ignore malformed frame */ }
		};
		// The browser reconnects by itself (sending Last-Event-ID); we only report it.
		es.onerror = () => handlers.onError();
		return {
			close: () => es.close(),
			get closed() { return es.readyState === EventSource.CLOSED; },
		};
	},
};
