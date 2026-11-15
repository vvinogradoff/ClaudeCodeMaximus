import { Marked } from 'marked';
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

/** Tags/attributes that can make the browser request a remote resource (the CSP allows https: media for the gated previews). */
const REMOTE_LOAD_TAGS = ['img', 'video', 'audio', 'source', 'picture', 'svg', 'math', 'object', 'embed', 'link', 'meta', 'track', 'base', 'frame', 'frameset', 'portal'];
const REMOTE_LOAD_ATTRS = ['srcset', 'background', 'poster', 'ping'];

const escapeText = (t: string) => t.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
/**
 * Transcript markdown: `![alt](url)` becomes a plain link (or just the alt text for a non-http(s) url), never an <img>. Media
 * is shown only through EntryExtras (allowlist auto-load, click-to-load otherwise), which the user can see and gate.
 */
const transcriptMarked = new Marked({
	gfm: true,
	breaks: false,
	renderer: {
		image: ({ href, text }) => {
			const label = escapeText(text || href || 'image');
			return /^https?:\/\//i.test(href ?? '') ? `<a href="${escapeText(href ?? '').replace(/"/g, '&quot;')}">${label}</a>` : label;
		},
	},
});

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
		FORBID_TAGS: ['form', 'input', 'textarea', 'select', 'button', 'style', 'iframe', ...REMOTE_LOAD_TAGS],
		FORBID_ATTR: ['style', 'formaction', ...REMOTE_LOAD_ATTRS],
	});
}

const cache = new Map<string, string>();
const CACHE_MAX = 300;

/** Markdown -> sanitised HTML. Tables are wrapped (horizontal scroll), fenced code gets a copy button. */
export function renderMarkdown(src: string): string {
	const hit = cache.get(src);
	if (hit !== undefined) return hit;
	ensureHook();
	let html = transcriptMarked.parse(src, { async: false }) as string;
	html = html
		.replace(/<table/g, '<div class="tw"><table')
		.replace(/<\/table>/g, '</table></div>')
		// The copy button is NOT part of the HTML: React adds it into div.code after sanitising (see Markdown.tsx).
		.replace(/<pre>/g, '<div class="code"><pre>')
		.replace(/<\/pre>/g, '</pre></div>');
	const clean = DOMPurify.sanitize(html, {
		ADD_ATTR: ['target'],
		// Model output is untrusted: no forms/controls (they could phish or post to another origin), no styles/overlays, no frames.
		FORBID_TAGS: ['form', 'input', 'textarea', 'select', 'button', 'style', 'iframe', ...REMOTE_LOAD_TAGS],
		FORBID_ATTR: ['style', 'formaction', ...REMOTE_LOAD_ATTRS],
	});
	if (cache.size >= CACHE_MAX) cache.clear();
	cache.set(src, clean);
	return clean;
}
