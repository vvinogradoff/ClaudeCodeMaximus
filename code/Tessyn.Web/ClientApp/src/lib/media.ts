// Pure helpers for transcript previews and the file preview sheet: link/path detection, host allowlist, file classification.

export type PreviewKind = 'markdown' | 'text' | 'image' | 'video' | 'pdf' | 'other';
export type MediaKind = 'image' | 'video';

/** Largest file the phone is asked to load per kind (bytes). The daemon enforces its own, larger caps. */
export const PREVIEW_LIMITS: Record<Exclude<PreviewKind, 'other'>, number> = {
	markdown: 2 * 1024 * 1024,
	text: 2 * 1024 * 1024,
	image: 30 * 1024 * 1024,
	video: 60 * 1024 * 1024,
	pdf: 50 * 1024 * 1024,
};

export const MAX_ATTACHMENT_BYTES = 200 * 1024 * 1024;
export const MAX_MEDIA_PER_ENTRY = 6;
export const MAX_PATHS_PER_ENTRY = 12;

export function formatBytes(n: number): string {
	if (!Number.isFinite(n) || n < 0) return '?';
	if (n < 1024) return `${n} B`;
	if (n < 1024 * 1024) return `${(n / 1024).toFixed(n < 10 * 1024 ? 1 : 0)} KB`;
	if (n < 1024 * 1024 * 1024) return `${(n / (1024 * 1024)).toFixed(n < 10 * 1024 * 1024 ? 1 : 0)} MB`;
	return `${(n / (1024 * 1024 * 1024)).toFixed(1)} GB`;
}

const IMAGE_EXT = new Set(['png', 'jpg', 'jpeg', 'gif', 'webp', 'bmp', 'avif']);
const VIDEO_EXT = new Set(['mp4', 'm4v', 'webm', 'mov']);
const MD_EXT = new Set(['md', 'markdown']);

export function extensionOf(name: string): string {
	const base = name.split(/[\\/]/).pop() ?? name;
	const i = base.lastIndexOf('.');
	return i > 0 ? base.slice(i + 1).toLowerCase() : '';
}

const MD_MIMES = new Set(['text/markdown', 'text/x-markdown', 'text/plain']);
const RASTER_MIMES = new Set(['image/png', 'image/jpeg', 'image/gif', 'image/webp', 'image/bmp', 'image/avif']);
const VIDEO_MIMES = new Set(['video/mp4', 'video/webm', 'video/quicktime', 'video/x-m4v']);
const TEXT_APP_MIMES = new Set(['application/json', 'application/xml', 'application/x-yaml', 'application/yaml', 'application/toml', 'application/javascript', 'application/x-sh', 'application/sql', 'image/svg+xml']);
const GENERIC_MIMES = new Set(['', 'application/octet-stream', 'binary/octet-stream']);

const CODE_EXT = new Set([
	'cs', 'ts', 'tsx', 'js', 'jsx', 'mjs', 'py', 'java', 'go', 'rs', 'c', 'h', 'cpp', 'hpp', 'rb', 'php', 'sh', 'ps1', 'bat', 'cmd', 'sql',
	'xaml', 'axaml', 'csproj', 'sln', 'props', 'targets', 'kt', 'swift', 'lua', 'r', 'scala', 'vue', 'scss', 'less', 'razor', 'cshtml', 'gradle',
]);
const TEXT_EXT = new Set(['txt', 'log', 'csv', 'json', 'jsonl', 'xml', 'yaml', 'yml', 'toml', 'ini', 'css', 'diff', 'patch', 'html', 'htm', 'svg']);

function textMimeOk(m: string): boolean {
	return GENERIC_MIMES.has(m) || m.startsWith('text/') || TEXT_APP_MIMES.has(m) || m.endsWith('+json') || m.endsWith('+xml');
}

/**
 * What the preview sheet does with a file. The EXTENSION decides, and the mime must be compatible with it (generic mimes pass);
 * a mismatch (`a.png` as `text/html`, `x.pdf` as `application/xhtml+xml`) is `other`, i.e. not previewed.
 * svg and html are deliberately TEXT: they are never rendered (scripts, external loads).
 */
export function classifyPreview(name: string, mime: string): PreviewKind {
	const ext = extensionOf(name);
	const m = mime.toLowerCase().split(';')[0].trim();
	if (MD_EXT.has(ext)) return GENERIC_MIMES.has(m) || MD_MIMES.has(m) ? 'markdown' : 'other';
	if (ext === 'pdf') return GENERIC_MIMES.has(m) || m === 'application/pdf' ? 'pdf' : 'other';
	if (IMAGE_EXT.has(ext)) return GENERIC_MIMES.has(m) || RASTER_MIMES.has(m) ? 'image' : 'other';
	if (VIDEO_EXT.has(ext)) return GENERIC_MIMES.has(m) || VIDEO_MIMES.has(m) ? 'video' : 'other';
	if (TEXT_EXT.has(ext) || CODE_EXT.has(ext)) return textMimeOk(m) ? 'text' : 'other';
	// Unknown extension: only a clearly safe mime decides.
	if (m === 'text/markdown' || m === 'text/x-markdown') return 'markdown';
	if (m === 'application/pdf') return 'pdf';
	if (RASTER_MIMES.has(m)) return 'image';
	if (VIDEO_MIMES.has(m)) return 'video';
	if (m.startsWith('text/') && m !== 'text/html') return 'text';
	if (m === 'application/json' || m.endsWith('+json')) return 'text';
	return 'other';
}

const BLOB_BY_EXT: Record<string, string> = {
	png: 'image/png', jpg: 'image/jpeg', jpeg: 'image/jpeg', gif: 'image/gif', webp: 'image/webp', bmp: 'image/bmp', avif: 'image/avif',
	mp4: 'video/mp4', m4v: 'video/mp4', webm: 'video/webm', mov: 'video/quicktime',
};

/**
 * Type of the blob built for image/video/pdf. Chosen by KIND, never passed through from the daemon or a ref: pdf is always
 * application/pdf, image and video only the allowlisted types (by extension, else by an exactly-allowlisted mime), anything
 * else application/octet-stream. A blob opened in a new tab therefore can never be an html/xml/svg document of our origin.
 */
export function blobMime(name: string, mime: string, kind: PreviewKind): string {
	if (kind === 'pdf') return 'application/pdf';
	const fromExt = BLOB_BY_EXT[extensionOf(name)];
	const m = mime.toLowerCase().split(';')[0].trim();
	if (kind === 'image') return fromExt?.startsWith('image/') ? fromExt : RASTER_MIMES.has(m) ? m : 'application/octet-stream';
	if (kind === 'video') return fromExt?.startsWith('video/') ? fromExt : VIDEO_MIMES.has(m) ? m : 'application/octet-stream';
	return 'application/octet-stream';
}

// ---- https media links ----------------------------------------------------------------------------------

export interface MediaLink { url: string; host: string; kind: MediaKind | 'unknown' }

const URL_RE = /https:\/\/[^\s<>"'`)\]]+/g;

function trimUrl(u: string): string {
	return u.replace(/[.,;:!?*_]+$/, '');
}

/** https URLs in `content` that look like images/videos (by extension) or sit on an allowlisted host (kind unknown). */
export function findMediaLinks(content: string, hosts: readonly string[] | undefined): MediaLink[] {
	const found = new Map<string, MediaLink>();
	for (const m of content.matchAll(URL_RE)) {
		const raw = trimUrl(m[0]);
		let u: URL;
		try { u = new URL(raw); } catch { continue; }
		if (u.protocol !== 'https:' || u.username || u.password) continue;
		const ext = extensionOf(u.pathname);
		const kind: MediaLink['kind'] | null = IMAGE_EXT.has(ext) ? 'image' : VIDEO_EXT.has(ext) ? 'video' : hostAllowed(u.hostname, hosts) && ext !== 'html' ? 'unknown' : null;
		if (!kind) continue;
		if (!found.has(u.href)) found.set(u.href, { url: u.href, host: u.hostname, kind });
		if (found.size >= MAX_MEDIA_PER_ENTRY) break;
	}
	return [...found.values()];
}

/** `host` matches exactly, `*.suffix` matches any sub-domain (not the bare suffix). Case-insensitive. */
export function hostAllowed(host: string, patterns: readonly string[] | undefined): boolean {
	if (!patterns || patterns.length === 0) return false;
	const h = host.toLowerCase();
	return patterns.some((p0) => {
		const p = p0.trim().toLowerCase();
		if (!p) return false;
		if (p.startsWith('*.')) return h.endsWith(p.slice(1)) && h.length > p.length - 1;
		return h === p;
	});
}

// ---- local file paths -----------------------------------------------------------------------------------

const BACKTICK_PATH_RE = /`((?:[A-Za-z]:[\\/]|\\\\[^\\/\s`]+[\\/])[^`\r\n]*?\.[A-Za-z0-9]{1,10})`/g;
const BARE_PATH_RE = /(?:^|[\s(\["'])((?:[A-Za-z]:[\\/]|\\\\[^\\/\s]+[\\/])[^\s<>"'`|*?]*)/g;

/** Absolute Windows paths (`C:\...`, `\\server\share\...`) that end in a file name with an extension, de-duplicated, in order. */
export function findLocalPaths(content: string): string[] {
	const out: string[] = [];
	const add = (p: string) => {
		const clean = p.replace(/[.,;:!?)\]]+$/, '');
		if (!/\.[A-Za-z0-9]{1,10}$/.test(clean)) return;
		if (!out.includes(clean)) out.push(clean);
	};
	for (const m of content.matchAll(BACKTICK_PATH_RE)) add(m[1]);
	const withoutTicks = content.replace(BACKTICK_PATH_RE, ' ');
	for (const m of withoutTicks.matchAll(BARE_PATH_RE)) add(m[1]);
	return out.slice(0, MAX_PATHS_PER_ENTRY);
}

export function fileNameOf(path: string): string {
	return path.split(/[\\/]/).filter((s) => s).pop() ?? path;
}
