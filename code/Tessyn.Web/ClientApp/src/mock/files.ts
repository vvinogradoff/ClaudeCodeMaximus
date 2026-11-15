// Files served by the mock daemon's file.read / file.preview (dev/test only, never part of the production bundle).
import type { FileReadResult } from '../api/types';

export interface MockFile {
	name: string;
	mime: string;
	/** Real content. */
	bytes: Uint8Array;
	/** Reported size when it differs from the content (to demonstrate the "too large" notice without allocating it). */
	claimedSize?: number;
	text: boolean;
}

/** Small on purpose, so that a normal file needs several chunks and the multi-chunk loop is exercised. */
export const MOCK_CHUNK_BYTES = 16 * 1024;

const enc = (s: string) => new TextEncoder().encode(s);

export const PLAN_MD = `# Migration plan

Awaiting your approval. **Nothing has been changed yet.**

## Steps

1. Back up the staging replica
2. Apply \`0042_add_tags\` with the new \`IX_Tags_Name\` index
3. Verify row counts, then promote

| Step | Risk | Rollback |
|---|---|---|
| Backup | none | n/a |
| Migration | medium | restore the backup |

\`\`\`sql
ALTER TABLE Tags ADD CONSTRAINT UQ_Tags_Name UNIQUE (Name);
\`\`\`

See the [runbook](https://example.com/runbook) and a [script link](javascript:alert(1)).

<script>alert('raw html must never run')</script>

<img src="https://tracker.example.com/pixel.png" alt="tracking pixel">
`;

export const NOTES_TXT = `Release notes - draft\n${'- item with a fairly long line to make the monospace wrap and scroll horizontally on a phone screen\n'.repeat(18)}Smile: ünïcödé ✓ 日本語\n`;

export const SNIPPET_CS = `using System;

namespace Demo;

public static class Greeter
{
	public static string Hello(string name) => $"Hello, {name}!";
}
`;

export const SVG_TEXT = `<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64"><script>alert('never rendered')</script><circle cx="32" cy="32" r="30" fill="tomato"/></svg>`;
export const HTML_TEXT = `<!doctype html><html><body><h1>Report</h1><script>alert('never rendered')</script></body></html>`;

function pngBytes(): Uint8Array {
	const c = document.createElement('canvas');
	c.width = 640;
	c.height = 360;
	const g = c.getContext('2d')!;
	const grad = g.createLinearGradient(0, 0, 640, 360);
	grad.addColorStop(0, '#4f46e5');
	grad.addColorStop(1, '#f97316');
	g.fillStyle = grad;
	g.fillRect(0, 0, 640, 360);
	g.fillStyle = 'rgba(255,255,255,0.9)';
	g.font = 'bold 40px sans-serif';
	g.fillText('Mock image', 190, 190);
	const b64 = c.toDataURL('image/png').split(',')[1];
	const bin = atob(b64);
	const out = new Uint8Array(bin.length);
	for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
	return out;
}

/** The PNG the Playwright tests serve for https image links. */
export function mockPngBytes(): Uint8Array { return pngBytes(); }

export function buildMockFiles(): Record<string, MockFile> {
	const fake = (n: number) => { const a = new Uint8Array(n); for (let i = 0; i < n; i++) a[i] = (i * 31) & 0xff; return a; };
	return {
		'plan.md': { name: 'plan.md', mime: 'text/markdown', bytes: enc(PLAN_MD), text: true },
		'notes.txt': { name: 'notes.txt', mime: 'text/plain', bytes: enc(NOTES_TXT.repeat(30)), text: true },
		'Greeter.cs': { name: 'Greeter.cs', mime: 'text/plain', bytes: enc(SNIPPET_CS), text: true },
		'diagram.svg': { name: 'diagram.svg', mime: 'image/svg+xml', bytes: enc(SVG_TEXT), text: false },
		'report.html': { name: 'report.html', mime: 'text/html', bytes: enc(HTML_TEXT), text: true },
		'photo.png': { name: 'photo.png', mime: 'image/png', bytes: pngBytes(), text: false },
		'clip.mp4': { name: 'clip.mp4', mime: 'video/mp4', bytes: fake(40 * 1024), text: false },
		'report.pdf': { name: 'report.pdf', mime: 'application/pdf', bytes: new Uint8Array([...enc('%PDF-1.4\n'), ...fake(30 * 1024)]), text: false },
		'huge.mp4': { name: 'huge.mp4', mime: 'video/mp4', bytes: fake(1024), claimedSize: 320 * 1024 * 1024, text: false },
	};
}

export function sizeOf(f: MockFile): number { return f.claimedSize ?? f.bytes.length; }

function toBase64(bytes: Uint8Array): string {
	let s = '';
	for (let i = 0; i < bytes.length; i += 0x8000) s += String.fromCharCode(...bytes.subarray(i, i + 0x8000));
	return btoa(s);
}

/** One file.read chunk. Text chunks are aligned to UTF-8 character boundaries like the daemon's. */
export function readChunk(f: MockFile, offset: number, length?: number): FileReadResult {
	const size = f.bytes.length;
	const want = Math.min(length ?? MOCK_CHUNK_BYTES, MOCK_CHUNK_BYTES);
	let start = Math.min(offset, size);
	let end = Math.min(size, start + want);
	if (f.text) {
		while (start < size && start > 0 && (f.bytes[start] & 0xc0) === 0x80) start++;
		while (end < size && end > start && (f.bytes[end] & 0xc0) === 0x80) end--;
	}
	const slice = f.bytes.subarray(start, end);
	return {
		name: f.name, mime: f.mime, size: sizeOf(f), offset: start, length: slice.length,
		encoding: f.text ? 'utf8' : 'base64',
		data: f.text ? new TextDecoder().decode(slice) : toBase64(slice),
		eof: end >= size,
	};
}
