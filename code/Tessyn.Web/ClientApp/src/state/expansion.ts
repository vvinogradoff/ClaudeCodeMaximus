import { useSyncExternalStore } from 'react';
import { readJson, writeJson } from '../lib/storage';

// Client-owned tree expansion state (directories, groups, supervisors), persisted in localStorage.
// Initialised from the daemon's isExpanded flag the first time a node is seen.

const KEY = 'expanded';
let map: Record<string, boolean> = readJson<Record<string, boolean>>(KEY, {});
const subs = new Set<() => void>();
let version = 0;

function emit(): void {
	version++;
	for (const s of subs) s();
}

export function isExpanded(id: string, initial: boolean): boolean {
	const v = map[id];
	return v === undefined ? initial : v;
}

export function setExpanded(id: string, value: boolean): void {
	if (map[id] === value) return;
	map = { ...map, [id]: value };
	writeJson(KEY, map);
	emit();
}

export function toggleExpanded(id: string, initial: boolean): void {
	setExpanded(id, !isExpanded(id, initial));
}

export function useExpansionVersion(): number {
	return useSyncExternalStore(
		(cb) => { subs.add(cb); return () => { subs.delete(cb); }; },
		() => version,
	);
}
