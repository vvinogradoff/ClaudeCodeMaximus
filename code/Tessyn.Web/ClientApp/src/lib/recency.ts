// Tree-row recency shading (desktop SessionNodeViewModel.RefreshRecencyBrush): bands by time since lastActivityUtc.

export type RecencyBand = 'strong' | 'light' | 'faint' | 'fainter' | 'none';

const MIN = 60_000;
export const RECENT_ACTIVE_MS = 24 * 60 * MIN;

/** <=15 min strong, <=60 lighter, <=240 faint, <=720 fainter, else none. `nowMs` is server-corrected "now". */
export function recencyBand(lastActivityUtc: string | undefined, nowMs: number): RecencyBand {
	if (!lastActivityUtc) return 'none';
	const t = Date.parse(lastActivityUtc);
	if (Number.isNaN(t)) return 'none';
	const age = Math.max(0, nowMs - t);
	if (age <= 15 * MIN) return 'strong';
	if (age <= 60 * MIN) return 'light';
	if (age <= 240 * MIN) return 'faint';
	if (age <= 720 * MIN) return 'fainter';
	return 'none';
}

/** Dotted border: active within the last 24 h. */
export function isRecentlyActive(lastActivityUtc: string | undefined, nowMs: number): boolean {
	if (!lastActivityUtc) return false;
	const t = Date.parse(lastActivityUtc);
	return !Number.isNaN(t) && nowMs - t <= RECENT_ACTIVE_MS;
}
