// Exact-toggle rules (FR.1.14): auto-on when the query starts with https:// or is shorter than 8 chars;
// unticking holds until the text changes; otherwise the last manual choice persists.

export const EXACT_AUTO_MAX_LENGTH = 8;
export const EXACT_AUTO_PREFIX = 'https://';

export interface ExactState {
	/** Last manual choice, used for non-auto queries. */
	manual: boolean;
	/** The trimmed query text the user unticked Exact for (while auto applies), or null. */
	uncheckedFor: string | null;
}

export const initialExactState: ExactState = { manual: false, uncheckedFor: null };

export function isAutoExact(query: string): boolean {
	const q = query.trim();
	return q.toLowerCase().startsWith(EXACT_AUTO_PREFIX) || q.length < EXACT_AUTO_MAX_LENGTH;
}

export function effectiveExact(query: string, st: ExactState): boolean {
	const q = query.trim();
	if (isAutoExact(q)) return st.uncheckedFor !== q;
	return st.manual;
}

export function toggleExact(query: string, st: ExactState, value: boolean): ExactState {
	const q = query.trim();
	if (isAutoExact(q)) return { ...st, uncheckedFor: value ? null : q };
	return { ...st, manual: value };
}
