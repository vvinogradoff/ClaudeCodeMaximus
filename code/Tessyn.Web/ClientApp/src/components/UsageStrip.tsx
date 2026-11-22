import { useCallback, useEffect, useState } from 'react';
import { rpc } from '../api/rpc';
import type { UsageResult } from '../api/types';
import { effectiveChoice, usageBars } from '../lib/engines';
import type { UsageBar } from '../lib/engines';
import { formatReset } from '../lib/format';
import { subscribeEvents } from '../state/bus';
import { nowMs } from '../state/appState';
import { useApp } from '../state/AppContext';

const REFRESH_MS = 5 * 60_000;

function Bar({ bar, now }: { bar: UsageBar; now: number }) {
	const w = bar.window;
	const pct = Math.max(0, Math.min(100, Math.round(w.utilization)));
	return (
		<div className={bar.exhausted ? 'usage-bar exhausted' : 'usage-bar'} title={bar.title} data-exhausted={bar.exhausted || undefined}>
			<div className="fill" style={{ width: `${pct}%` }} />
			<span className="usage-text">{bar.name}: {pct}%{!w.isStale && <>  resets {formatReset(w.resetsAtUtc, now)}</>}</span>
		</div>
	);
}

export function UsageStrip() {
	const { state } = useApp();
	const settings = state.session?.settings ?? null;
	// FR.33.10: a GPT session shows the plan windows of its ChatGPT profile, the profile its next send uses.
	const profileId = settings ? (effectiveChoice(state.catalog, settings, state.session?.detail?.state.engine).profileId ?? null) : null;
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
			{usageBars(usage).map((b) => <Bar key={b.name} bar={b} now={now} />)}
		</footer>
	);
}
