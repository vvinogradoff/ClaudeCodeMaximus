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

/**
 * What the preview sheet does with a file. svg and html are deliberately TEXT: they are never rendered
 * (scripts, external loads). Anything not clearly an image/video/pdf/markdown that is text-ish is shown as text.
 */
export function classifyPreview(name: string, mime: string): PreviewKind {
	const ext = extensionOf(name);
	const m = mime.toLowerCase();
	if (ext === 'svg' || m === 'image/svg+xml' || ext === 'html' || ext === 'htm' || m === 'text/html') return 'text';
	if (MD_EXT.has(ext) || m === 'text/markdown') return 'markdown';
	if (m === 'application/pdf' || ext === 'pdf') return 'pdf';
	if (IMAGE_EXT.has(ext) || m.startsWith('image/')) return 'image';
	if (VIDEO_EXT.has(ext) || m.startsWith('video/')) return 'video';
	if (m.startsWith('text/') || m === 'application/json' || m === 'application/xml' || m.endsWith('+json') || m.endsWith('+xml')) return 'text';
	if (ext === 'mkv') return 'other';
	if (['txt', 'log', 'csv', 'json', 'jsonl', 'xml', 'yaml', 'yml', 'toml', 'ini', 'css', 'diff', 'patch'].includes(ext)) return 'text';
	if (m === '' || m === 'application/octet-stream') return CODE_EXT.has(ext) ? 'text' : 'other';
	return CODE_EXT.has(ext) ? 'text' : 'other';
}

const CODE_EXT = new Set([
	'cs', 'ts', 'tsx', 'js', 'jsx', 'mjs', 'py', 'java', 'go', 'rs', 'c', 'h', 'cpp', 'hpp', 'rb', 'php', 'sh', 'ps1', 'bat', 'cmd', 'sql',
	'xaml', 'axaml', 'csproj', 'sln', 'props', 'targets', 'kt', 'swift', 'lua', 'r', 'scala', 'vue', 'scss', 'less', 'razor', 'cshtml', 'gradle',
]);

/** Mime used for blobs (image/video/pdf) when the daemon's mime is missing or generic. */
export function blobMime(name: string, mime: string, kind: PreviewKind): string {
	if (mime && mime !== 'application/octet-stream') return mime;
	const ext = extensionOf(name);
	if (kind === 'pdf') return 'application/pdf';
	if (kind === 'image') return ext === 'jpg' ? 'image/jpeg' : `image/${ext || 'png'}`;
	if (kind === 'video') return ext === 'mov' ? 'video/quicktime' : `video/${ext || 'mp4'}`;
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
