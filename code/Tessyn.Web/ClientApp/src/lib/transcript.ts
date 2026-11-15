import type { SessionEntriesResult, SessionEntryDto } from '../api/types';

export interface TranscriptState {
	nodeId: string | null;
	epoch: number;
	total: number;
	entries: SessionEntryDto[];
	/** True when entries before the first loaded one exist. */
	hasEarlier: boolean;
	loaded: boolean;
	/**
	 * True while the loaded page is a window around a referenced entry (session.entries aroundEntryId) that does not reach the
	 * end of the session: live appends are not fetched (they would load everything in between) and "Load later" is offered.
	 */
	anchored: boolean;
}

export const emptyTranscript: TranscriptState = { nodeId: null, epoch: 0, total: 0, entries: [], hasEarlier: false, loaded: false, anchored: false };

export function replaceTranscript(r: SessionEntriesResult, anchored = false): TranscriptState {
	const last = r.entries.length > 0 ? r.entries[r.entries.length - 1].index : -1;
	return {
		nodeId: r.nodeId,
		epoch: r.epoch,
		total: r.total,
		entries: r.entries,
		hasEarlier: r.entries.length > 0 && r.entries[0].index > 0,
		loaded: true,
		anchored: anchored && last < r.total - 1,
	};
}

/** Index of the entry with this stable id in the loaded page (by id, falling back to the ordinal after the colon). */
export function indexOfEntryId(entries: SessionEntryDto[], entryId: string): number {
	const byId = entries.find((e) => e.entryId === entryId);
	if (byId) return byId.index;
	const ordinal = Number(entryId.split(':').pop());
	return Number.isInteger(ordinal) && entries.some((e) => e.index === ordinal) ? ordinal : -1;
}

/** Merge entries fetched with afterIndex; ignores results for another session/epoch and duplicates. */
export function appendEntries(t: TranscriptState, r: SessionEntriesResult): TranscriptState {
	if (t.nodeId !== r.nodeId || t.epoch !== r.epoch) return t;
	const last = t.entries.length > 0 ? t.entries[t.entries.length - 1].index : -1;
	const fresh = r.entries.filter((e) => e.index > last);
	const entries = fresh.length > 0 ? [...t.entries, ...fresh] : t.entries;
	const end = entries.length > 0 ? entries[entries.length - 1].index : -1;
	return { ...t, total: r.total, entries, anchored: t.anchored && end < r.total - 1 };
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
