const p2 = (n: number) => String(n).padStart(2, '0');

/** yyyy-MM-dd HH:mm in local time (tree rows, last prompt). */
export function formatDateTime(iso: string | undefined): string {
	if (!iso) return '';
	const d = new Date(iso);
	if (Number.isNaN(d.getTime())) return '';
	return `${d.getFullYear()}-${p2(d.getMonth() + 1)}-${p2(d.getDate())} ${p2(d.getHours())}:${p2(d.getMinutes())}`;
}

export function formatTime(iso: string | undefined): string {
	if (!iso) return '';
	const d = new Date(iso);
	if (Number.isNaN(d.getTime())) return '';
	return `${p2(d.getHours())}:${p2(d.getMinutes())}`;
}

/** HH:mm today, otherwise yyyy-MM-dd HH:mm. */
export function formatSmartTime(iso: string | undefined, nowMs: number): string {
	if (!iso) return '';
	const d = new Date(iso);
	if (Number.isNaN(d.getTime())) return '';
	const n = new Date(nowMs);
	const same = d.getFullYear() === n.getFullYear() && d.getMonth() === n.getMonth() && d.getDate() === n.getDate();
	return same ? formatTime(iso) : formatDateTime(iso);
}

export function formatElapsed(ms: number): string {
	const s = Math.max(0, Math.floor(ms / 1000));
	return `${Math.floor(s / 60)}:${p2(s % 60)}`;
}

const DAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];

/** "14:20" when the reset is within a day, else the weekday ("Mon"). */
export function formatReset(iso: string, nowMs: number): string {
	const d = new Date(iso);
	if (Number.isNaN(d.getTime())) return '';
	if (d.getTime() - nowMs < 24 * 3600_000) return formatTime(iso);
	return DAYS[d.getDay()];
}
