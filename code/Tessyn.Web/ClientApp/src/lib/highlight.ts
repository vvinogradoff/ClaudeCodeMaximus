// Locating a Focus entry-ref quote inside the rendered text of an entry.
// The daemon gives `offset`/`length` in characters of the entry's raw `content`. Markdown rendering drops markup, so for rendered
// (assistant) entries we search the quote in the DOM text with whitespace collapsed; plain entries use the exact offsets.

/** Collapses whitespace runs to one space and records, for every kept character, its index in the source. */
export function collapseWhitespace(src: string): { text: string; map: number[] } {
	let text = '';
	const map: number[] = [];
	let prevSpace = false;
	for (let i = 0; i < src.length; i++) {
		const isSpace = /\s/.test(src[i]);
		if (isSpace) {
			if (prevSpace) continue;
			text += ' ';
			prevSpace = true;
		} else {
			text += src[i];
			prevSpace = false;
		}
		map.push(i);
	}
	return { text, map };
}

/** Quote text taken from the raw content, or '' when offset/length are out of range. */
export function quoteOf(content: string, offset: number, length: number): string {
	if (offset < 0 || length <= 0 || offset > content.length) return '';
	return content.slice(offset, Math.min(content.length, offset + length));
}

/** Markup characters a markdown renderer removes; stripped from the quote as a second attempt. */
const MARKUP = /[*_`#>~|]/g;

/** [start, end) of `quote` inside `text`, comparing with whitespace collapsed; null when not found. */
export function findInText(text: string, quote: string): { start: number; end: number } | null {
	const hay = collapseWhitespace(text);
	for (const q of [quote, quote.replace(MARKUP, '')]) {
		const needle = collapseWhitespace(q).text.trim();
		if (!needle) continue;
		const at = hay.text.indexOf(needle);
		if (at >= 0) return { start: hay.map[at], end: hay.map[at + needle.length - 1] + 1 };
	}
	return null;
}

/** A DOM Range around `quote` inside `root`'s text nodes, or null. */
export function findQuoteRange(root: Node, quote: string, doc: Document = document): Range | null {
	// The React-added Copy buttons live inside the rendered HTML; their text is not part of the message.
	const walker = doc.createTreeWalker(root, NodeFilter.SHOW_TEXT, {
		acceptNode: (n) => (n.parentElement?.closest('button') ? NodeFilter.FILTER_REJECT : NodeFilter.FILTER_ACCEPT),
	});
	const nodes: Text[] = [];
	let full = '';
	const starts: number[] = [];
	for (let n = walker.nextNode(); n; n = walker.nextNode()) {
		starts.push(full.length);
		nodes.push(n as Text);
		full += (n as Text).data;
	}
	const hit = findInText(full, quote);
	if (!hit) return null;
	// A start position belongs to the node that contains that character; an end position to the node that contains the one before it.
	const locate = (pos: number, atEnd: boolean): { node: Text; offset: number } | null => {
		for (let i = 0; i < nodes.length; i++) {
			const s = starts[i];
			const e = s + nodes[i].data.length;
			if (atEnd ? pos > s && pos <= e : pos >= s && pos < e) return { node: nodes[i], offset: pos - s };
		}
		return null;
	};
	const a = locate(hit.start, false);
	const b = locate(hit.end, true);
	if (!a || !b) return null;
	const r = doc.createRange();
	r.setStart(a.node, a.offset);
	r.setEnd(b.node, b.offset);
	return r;
}
