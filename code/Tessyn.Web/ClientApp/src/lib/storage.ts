import { useCallback, useState } from 'react';

const PREFIX = 'tessyn.';

export function readJson<T>(key: string, fallback: T): T {
	try {
		const raw = localStorage.getItem(PREFIX + key);
		return raw === null ? fallback : (JSON.parse(raw) as T);
	} catch {
		return fallback;
	}
}

export function writeJson(key: string, value: unknown): void {
	try { localStorage.setItem(PREFIX + key, JSON.stringify(value)); } catch { /* storage full / disabled */ }
}

export function removeKey(key: string): void {
	try { localStorage.removeItem(PREFIX + key); } catch { /* ignore */ }
}

export function readText(key: string): string {
	try { return localStorage.getItem(PREFIX + key) ?? ''; } catch { return ''; }
}

export function writeText(key: string, value: string): void {
	try {
		if (value === '') localStorage.removeItem(PREFIX + key);
		else localStorage.setItem(PREFIX + key, value);
	} catch { /* ignore */ }
}

/** useState persisted in localStorage. */
export function usePersistedState<T>(key: string, initial: T): [T, (v: T | ((prev: T) => T)) => void] {
	const [value, setValue] = useState<T>(() => readJson<T>(key, initial));
	const set = useCallback((v: T | ((prev: T) => T)) => {
		setValue((prev) => {
			const next = typeof v === 'function' ? (v as (p: T) => T)(prev) : v;
			writeJson(key, next);
			return next;
		});
	}, [key]);
	return [value, set];
}
