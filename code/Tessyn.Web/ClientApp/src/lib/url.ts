/** Returns the URL only when it parses and uses http: or https: (Focus url refs may be plain http). */
export function safeHttpUrl(url: string | undefined | null): string | undefined {
	if (!url) return undefined;
	try {
		const p = new URL(url).protocol;
		return p === 'https:' || p === 'http:' ? url : undefined;
	} catch {
		return undefined;
	}
}

/** Returns the URL only when it parses and uses https:; anything else (javascript:, data:, http:, garbage) yields undefined. */
export function safeHttpsUrl(url: string | undefined | null): string | undefined {
	if (!url) return undefined;
	try {
		return new URL(url).protocol === 'https:' ? url : undefined;
	} catch {
		return undefined;
	}
}
