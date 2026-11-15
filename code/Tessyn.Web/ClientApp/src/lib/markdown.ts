import { Marked, marked } from 'marked';
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

const escapeText = (t: string) => t.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
/** Own marked instance (the shared one renders raw HTML and images for transcripts). */
const fileMarked = new Marked({
	gfm: true,
	breaks: false,
	renderer: {
		html: ({ text }) => escapeText(text),
		image: ({ text }) => escapeText(text),
	},
});

/**
 * Markdown of a FILE (the file preview sheet) -> sanitised HTML, stricter than the transcript renderer: no raw HTML at all
 * (it is escaped and shown as text), no images (remote loads; the alt text is kept), links only http(s)/mailto and always
 * opened in a new tab with noopener noreferrer. Relative and `#` links are dropped (they would navigate the SPA).
 */
export function renderFileMarkdown(src: string): string {
	ensureHook();
	const html = fileMarked.parse(src, { async: false }) as string;
	const wrapped = html
		.replace(/<table/g, '<div class="tw"><table')
		.replace(/<\/table>/g, '</table></div>')
		.replace(/<pre>/g, '<div class="code"><pre>')
		.replace(/<\/pre>/g, '</pre></div>');
	return DOMPurify.sanitize(wrapped, {
		ADD_ATTR: ['target'],
		ALLOWED_URI_REGEXP: /^(?:https?:|mailto:)/i,
		FORBID_TAGS: ['form', 'input', 'textarea', 'select', 'button', 'style', 'iframe', 'img', 'svg', 'math', 'video', 'audio', 'source', 'object', 'embed', 'link', 'meta'],
		FORBID_ATTR: ['style', 'formaction', 'srcset'],
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
