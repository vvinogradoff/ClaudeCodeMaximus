import { setTransport } from '../api/rpc';
import { createMockTransport } from './daemon';

/** Installs the mock transport and exposes a small test hook (window.__mock) for the Playwright flows. */
export function installMock(): void {
	const { transport, daemon } = createMockTransport();
	setTransport(transport);
	(window as unknown as { __mock: unknown }).__mock = {
		setOnline: (v: boolean) => daemon.setOnline(v),
		emit: (m: string, p?: unknown) => daemon.emit(m, p),
		liveId: () => daemon.liveId,
		compactSilently: (name: string) => daemon.compactSilently(name),
	};
}
