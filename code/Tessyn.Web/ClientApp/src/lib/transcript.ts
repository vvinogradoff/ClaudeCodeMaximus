import type { SessionEntriesResult, SessionEntryDto } from '../api/types';

export interface TranscriptState {
	nodeId: string | null;
	epoch: number;
	total: number;
	entries: SessionEntryDto[];
	/** True when entries before the first loaded one exist. */
	hasEarlier: boolean;
	loaded: boolean;
}

export const emptyTranscript: TranscriptState = { nodeId: null, epoch: 0, total: 0, entries: [], hasEarlier: false, loaded: false };

export function replaceTranscript(r: SessionEntriesResult): TranscriptState {
	return {
		nodeId: r.nodeId,
		epoch: r.epoch,
		total: r.total,
		entries: r.entries,
		hasEarlier: r.entries.length > 0 && r.entries[0].index > 0,
		loaded: true,
	};
}

/** Merge entries fetched with afterIndex; ignores results for another session/epoch and duplicates. */
export function appendEntries(t: TranscriptState, r: SessionEntriesResult): TranscriptState {
	if (t.nodeId !== r.nodeId || t.epoch !== r.epoch) return t;
	const last = t.entries.length > 0 ? t.entries[t.entries.length - 1].index : -1;
	const fresh = r.entries.filter((e) => e.index > last);
	return { ...t, total: r.total, entries: fresh.length > 0 ? [...t.entries, ...fresh] : t.entries };
}

export function prependEntries(t: TranscriptState, r: SessionEntriesResult): TranscriptState {
	if (t.nodeId !== r.nodeId || t.epoch !== r.epoch) return t;
	const first = t.entries.length > 0 ? t.entries[0].index : Number.POSITIVE_INFINITY;
	const older = r.entries.filter((e) => e.index < first);
	const entries = [...older, ...t.entries];
	return { ...t, entries, hasEarlier: entries.length > 0 && entries[0].index > 0 };
}

export function replaceEntry(t: TranscriptState, entry: SessionEntryDto): TranscriptState {
	return { ...t, entries: t.entries.map((e) => (e.index === entry.index ? entry : e)) };
}

export type AppendedPlan = 'ignore' | 'reload' | 'fetchAfter';

/** What to do on `session.appended { nodeId, epoch }` / `session.reset`. */
export function planAppended(t: TranscriptState, openId: string | null, nodeId: string, epoch: number): AppendedPlan {
	if (openId !== nodeId) return 'ignore';
	if (!t.loaded || t.nodeId !== nodeId || t.epoch !== epoch) return 'reload';
	return 'fetchAfter';
}

/** Entries to display: whitespace-only entries are hidden except compaction. */
export function visibleEntries(entries: SessionEntryDto[]): SessionEntryDto[] {
	return entries.filter((e) => e.role === 'compaction' || e.content.trim().length > 0);
}
