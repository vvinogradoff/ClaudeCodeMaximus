import type { EntrySource } from '../api/types';

/** Inputs a person did not type: shown teal, with their own header label (the desktop client does the same). */
const AUTOMATED: Partial<Record<EntrySource, string>> = { scheduled: 'Scheduled', orchestrated: 'Supervisor' };

/** The header label of a user entry: "Scheduled" / "Supervisor" for an automated input, "You" otherwise (also for the web and Telegram). */
export function inputLabel(source: EntrySource | undefined): string {
	return (source && AUTOMATED[source]) || 'You';
}

/** True for a scheduled or supervisor input; these get the `auto` class on the message row. */
export function isAutomatedInput(source: EntrySource | undefined): boolean {
	return !!source && source in AUTOMATED;
}
