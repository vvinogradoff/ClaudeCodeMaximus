import { marked } from 'marked';
import DOMPurify from 'dompurify';

let hooked = false;
function ensureHook(): void {
	if (hooked) return;
	hooked = true;
	DOMPurify.addHook('afterSanitizeAttributes', (node) => {
		if (node.tagName === 'A') {
			node.setAttribute('target', '_blank');
			node.setAttribute('rel', 'noopener noreferrer');
		}
	});
}

const cache = new Map<string, string>();
const CACHE_MAX = 300;

/** Markdown -> sanitised HTML. Tables are wrapped (horizontal scroll), fenced code gets a copy button. */
export function renderMarkdown(src: string): string {
	const hit = cache.get(src);
	if (hit !== undefined) return hit;
	ensureHook();
	let html = marked.parse(src, { async: false, gfm: true, breaks: false }) as string;
	html = html
		.replace(/<table/g, '<div class="tw"><table')
		.replace(/<\/table>/g, '</table></div>')
		// The copy button is NOT part of the HTML: React adds it into div.code after sanitising (see Markdown.tsx).
		.replace(/<pre>/g, '<div class="code"><pre>')
		.replace(/<\/pre>/g, '</pre></div>');
	const clean = DOMPurify.sanitize(html, {
		ADD_ATTR: ['target'],
		// Model output is untrusted: no forms/controls (they could phish or post to another origin), no styles/overlays, no frames.
		FORBID_TAGS: ['form', 'input', 'textarea', 'select', 'button', 'style', 'iframe'],
		FORBID_ATTR: ['style', 'formaction'],
	});
	if (cache.size >= CACHE_MAX) cache.clear();
	cache.set(src, clean);
	return clean;
}
