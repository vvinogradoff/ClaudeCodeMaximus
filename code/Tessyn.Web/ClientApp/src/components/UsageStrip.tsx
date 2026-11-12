import { useCallback, useEffect, useState } from 'react';
import { rpc } from '../api/rpc';
import type { UsageResult, UsageWindowDto } from '../api/types';
import { formatReset } from '../lib/format';
import { subscribeEvents } from '../state/bus';
import { nowMs } from '../state/appState';
import { useApp } from '../state/AppContext';

const REFRESH_MS = 5 * 60_000;

function Bar({ name, w, now }: { name: string; w: UsageWindowDto; now: number }) {
	const pct = Math.max(0, Math.min(100, Math.round(w.utilization)));
	return (
		<div className="usage-bar" title={`${name}: ${pct}%`}>
			<div className="fill" style={{ width: `${pct}%` }} />
			<span className="usage-text">{name}: {pct}%{!w.isStale && <>  resets {formatReset(w.resetsAtUtc, now)}</>}</span>
		</div>
	);
}

export function UsageStrip() {
	const { state } = useApp();
	const profileId = state.session?.settings?.profileId ?? null;
	const sessionOpen = !!state.session;
	const [usage, setUsage] = useState<UsageResult | null>(null);

	const load = useCallback(() => {
		if (!sessionOpen || profileId === null) return;
		void rpc<UsageResult>('usage.get', { profileId }).then(setUsage).catch(() => setUsage(null));
	}, [sessionOpen, profileId]);

	useEffect(() => {
		load();
		const timer = window.setInterval(() => { if (document.visibilityState === 'visible') load(); }, REFRESH_MS);
		const off = subscribeEvents((f) => {
			if (f.method === 'resync') load();
			else if (f.method === 'usage.changed' && (f.params as { profileId?: string } | undefined)?.profileId === profileId) load();
		});
		return () => { window.clearInterval(timer); off(); };
	}, [load, profileId]);

	if (!sessionOpen || !usage || usage.state !== 'ok') return <footer className="usage hidden" />;
	const now = nowMs(state);
	return (
		<footer className="usage" data-testid="usage-strip">
			{usage.fiveHour && <Bar name="5h" w={usage.fiveHour} now={now} />}
			{usage.sevenDay && <Bar name="7d" w={usage.sevenDay} now={now} />}
		</footer>
	);
}
