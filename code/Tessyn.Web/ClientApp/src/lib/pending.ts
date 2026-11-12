import type { SessionEntryDto } from '../api/types';

/** A message the user sent that has not yet shown up as a USER entry in the transcript. */
export interface PendingMsg {
	id: string;
	text: string;
	queued: boolean;
	/** Index of the last known entry when it was sent; only USER entries after it can fulfil it. */
	baseIndex: number;
	createdAt: number;
}

const norm = (s: string) => s.replace(/\r\n/g, '\n').trim();

/** Drops pending messages that now have a matching USER entry (each entry fulfils at most one pending message). */
export function reconcilePending(pending: PendingMsg[], entries: SessionEntryDto[]): PendingMsg[] {
	if (pending.length === 0) return pending;
	const claimed = new Set<number>();
	const remaining: PendingMsg[] = [];
	for (const p of pending) {
		const hit = entries.find((e) => e.role === 'user' && e.index > p.baseIndex && !claimed.has(e.index) && norm(e.content) === norm(p.text));
		if (hit) claimed.add(hit.index);
		else remaining.push(p);
	}
	return remaining.length === pending.length ? pending : remaining;
}
