import { useEffect, useRef, useState } from 'react';
import { subscribeEvents } from '../state/bus';

export function useDebounced<T>(value: T, ms: number): T {
	const [v, setV] = useState(value);
	useEffect(() => {
		const t = window.setTimeout(() => setV(value), ms);
		return () => window.clearTimeout(t);
	}, [value, ms]);
	return v;
}

/** Calls `fn` (debounced) whenever one of the named daemon events arrives. */
export function useEventTrigger(methods: string[], fn: () => void, ms = 400): void {
	const ref = useRef(fn);
	ref.current = fn;
	const key = methods.join('|');
	useEffect(() => {
		let timer: number | undefined;
		const set = new Set(key.split('|'));
		const off = subscribeEvents((f) => {
			if (!set.has(f.method)) return;
			window.clearTimeout(timer);
			timer = window.setTimeout(() => ref.current(), ms);
		});
		return () => { off(); window.clearTimeout(timer); };
	}, [key, ms]);
}
