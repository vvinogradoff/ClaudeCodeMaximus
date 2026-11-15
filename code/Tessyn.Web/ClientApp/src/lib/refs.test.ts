// @vitest-environment jsdom
import { describe, expect, it } from 'vitest';
import type { FileReadResult, SessionEntriesResult, SessionEntryDto } from '../api/types';
import { initialState, reduce } from '../state/appState';
import { attachmentProblem, insertAttachedLine } from './attach';
import { base64ToBytes, readBlob, ReadCancelled, readText } from './fileRead';
import { collapseWhitespace, findInText, findQuoteRange, quoteOf } from './highlight';
import { renderFileMarkdown } from './markdown';
import { blobMime, classifyPreview, findLocalPaths, findMediaLinks, formatBytes, hostAllowed, MAX_ATTACHMENT_BYTES } from './media';
import { backLabel, NAV_MAX, popFrame, pushFrame, topFrame } from './navStack';
import type { NavFrame } from './navStack';
import { appendEntries, indexOfEntryId, replaceTranscript } from './transcript';

const entry = (index: number, content = `c${index}`): SessionEntryDto => ({ index, timestamp: '2026-01-01T00:00:00Z', role: 'assistant', content, entryId: `f:${index}` });
const result = (entries: SessionEntryDto[], total: number): SessionEntriesResult => ({ nodeId: 'n', epoch: 1, total, entries });
const frame = (nodeId: string, tab: NavFrame['tab'] = 'session', anchorEntryId?: string): NavFrame => ({ nodeId, title: nodeId, tab, anchorEntryId });

describe('preview classification', () => {
	it('renders markdown, shows svg and html as TEXT (never rendered)', () => {
		expect(classifyPreview('plan.md', 'text/markdown')).toBe('markdown');
		expect(classifyPreview('x.markdown', '')).toBe('markdown');
		expect(classifyPreview('a.svg', 'image/svg+xml')).toBe('text');
		expect(classifyPreview('a.SVG', 'image/png')).toBe('text');
		expect(classifyPreview('page.html', 'text/html')).toBe('text');
		expect(classifyPreview('page.htm', 'application/octet-stream')).toBe('text');
	});
	it('maps images, video, pdf, text and code', () => {
		expect(classifyPreview('a.png', 'image/png')).toBe('image');
		expect(classifyPreview('a.jpg', '')).toBe('image');
		expect(classifyPreview('a.mp4', 'video/mp4')).toBe('video');
		expect(classifyPreview('a.mov', '')).toBe('video');
		expect(classifyPreview('a.pdf', 'application/pdf')).toBe('pdf');
		expect(classifyPreview('a.txt', 'text/plain')).toBe('text');
		expect(classifyPreview('Greeter.cs', 'application/octet-stream')).toBe('text');
		expect(classifyPreview('data.json', 'application/json')).toBe('text');
		expect(classifyPreview('movie.mkv', 'video/x-matroska')).toBe('video');
		expect(classifyPreview('a.bin', 'application/octet-stream')).toBe('other');
		expect(classifyPreview('a.zip', 'application/zip')).toBe('other');
	});
	it('picks a blob mime when the daemon sends a generic one', () => {
		expect(blobMime('a.jpg', 'application/octet-stream', 'image')).toBe('image/jpeg');
		expect(blobMime('a.mp4', '', 'video')).toBe('video/mp4');
		expect(blobMime('a.pdf', '', 'pdf')).toBe('application/pdf');
		expect(blobMime('a.png', 'image/png', 'image')).toBe('image/png');
	});
	it('formats sizes', () => {
		expect(formatBytes(512)).toBe('512 B');
		expect(formatBytes(2048)).toBe('2.0 KB');
		expect(formatBytes(5 * 1024 * 1024)).toBe('5.0 MB');
		expect(formatBytes(320 * 1024 * 1024)).toBe('320 MB');
	});
});

describe('media host allowlist', () => {
	const hosts = ['annielartens.online', '*.githubusercontent.com'];
	it('matches exact hosts and sub-domains of a wildcard, case-insensitively', () => {
		expect(hostAllowed('annielartens.online', hosts)).toBe(true);
		expect(hostAllowed('ANNIELARTENS.online', hosts)).toBe(true);
		expect(hostAllowed('raw.githubusercontent.com', hosts)).toBe(true);
		expect(hostAllowed('githubusercontent.com', hosts)).toBe(false);
		expect(hostAllowed('evilgithubusercontent.com', hosts)).toBe(false);
		expect(hostAllowed('sub.annielartens.online', hosts)).toBe(false);
	});
	it('treats a missing or empty list as "nothing allowed"', () => {
		expect(hostAllowed('annielartens.online', undefined)).toBe(false);
		expect(hostAllowed('annielartens.online', [])).toBe(false);
	});
});

describe('findMediaLinks', () => {
	const hosts = ['annielartens.online'];
	it('finds https images and video by extension on any host', () => {
		const l = findMediaLinks('see https://x.example.org/a/b.PNG, and (https://y.example.org/c.mp4).', hosts);
		expect(l.map((m) => [m.host, m.kind])).toEqual([['x.example.org', 'image'], ['y.example.org', 'video']]);
	});
	it('keeps an allowlisted host without an extension as unknown, ignores other pages', () => {
		const l = findMediaLinks('https://annielartens.online/r/abc123 and https://example.com/page and https://annielartens.online/index.html', hosts);
		expect(l).toHaveLength(1);
		expect(l[0].kind).toBe('unknown');
	});
	it('ignores http, credentials and non-links; de-duplicates; caps the count', () => {
		expect(findMediaLinks('http://a.example/x.png', hosts)).toEqual([]);
		expect(findMediaLinks('https://user:pw@a.example/x.png', hosts)).toEqual([]);
		expect(findMediaLinks('https://a.example/x.png https://a.example/x.png', hosts)).toHaveLength(1);
		const many = Array.from({ length: 20 }, (_, i) => `https://a.example/${i}.png`).join(' ');
		expect(findMediaLinks(many, hosts)).toHaveLength(6);
	});
});

describe('findLocalPaths', () => {
	it('finds absolute Windows paths with an extension, in backticks, bare and UNC', () => {
		const p = findLocalPaths('Wrote `C:\\Program Files\\App\\plan.md` and C:\\Temp\\photo.png, also \\\\srv\\share\\doc.pdf. Not a file: C:\\Temp\\folder');
		expect(p).toEqual(['C:\\Program Files\\App\\plan.md', 'C:\\Temp\\photo.png', '\\\\srv\\share\\doc.pdf']);
	});
	it('accepts forward slashes, strips trailing punctuation and de-duplicates', () => {
		expect(findLocalPaths('(c:/temp/Greeter.cs). Again c:/temp/Greeter.cs;')).toEqual(['c:/temp/Greeter.cs']);
	});
	it('ignores relative paths, urls and plain text', () => {
		expect(findLocalPaths('docs/plan.md, https://example.com/a.png, nothing here')).toEqual([]);
	});
});

describe('highlighting a quote', () => {
	it('collapses whitespace with a position map', () => {
		const c = collapseWhitespace('a  b\n\nc');
		expect(c.text).toBe('a b c');
		expect(c.map).toEqual([0, 1, 3, 4, 6]);
	});
	it('quoteOf slices by offset/length and tolerates bad ranges', () => {
		expect(quoteOf('hello world', 6, 5)).toBe('world');
		expect(quoteOf('hello', 3, 99)).toBe('lo');
		expect(quoteOf('hello', 99, 3)).toBe('');
		expect(quoteOf('hello', 0, 0)).toBe('');
	});
	it('finds a whitespace-normalised quote and maps back to the source span', () => {
		const text = 'one   two\nthree four';
		const hit = findInText(text, 'two three');
		expect(hit && text.slice(hit.start, hit.end)).toBe('two\nthree');
		expect(findInText(text, 'absent')).toBeNull();
	});
	it('finds a quote in rendered text even when the raw quote carried markup', () => {
		expect(findInText('The migration touches 3 tables', '**migration** touches')).not.toBeNull();
	});
	it('builds a DOM Range across text nodes', () => {
		const root = document.createElement('div');
		root.innerHTML = '<p>The <strong>typed</strong> JSON-RPC client plus an SSE event stream.</p>';
		const r = findQuoteRange(root, 'typed JSON-RPC client plus an SSE');
		expect(r?.toString()).toBe('typed JSON-RPC client plus an SSE');
		expect(findQuoteRange(root, 'not in there')).toBeNull();
	});
});

describe('back navigation stack', () => {
	it('unwinds nested jumps one step at a time', () => {
		let s: NavFrame[] = [];
		s = pushFrame(s, frame('A', 'focus'));
		s = pushFrame(s, frame('B', 'session', 'f:7'));
		expect(topFrame(s)?.nodeId).toBe('B');
		s = popFrame(s);
		expect(topFrame(s)?.nodeId).toBe('A');
		s = popFrame(s);
		expect(topFrame(s)).toBeUndefined();
		expect(popFrame(s)).toEqual([]);
	});
	it('is bounded and labels frames', () => {
		let s: NavFrame[] = [];
		for (let i = 0; i < NAV_MAX + 10; i++) s = pushFrame(s, frame(`n${i}`));
		expect(s).toHaveLength(NAV_MAX);
		expect(s[0].nodeId).toBe('n10');
		expect(backLabel(frame('Plan', 'focus'))).toBe('Back to Plan · Focus');
		expect(backLabel(frame('Plan'))).toBe('Back to Plan');
	});
	it('the reducer keeps the stack across jumps, clears on close and on navClear', () => {
		let st = initialState('tree');
		st = reduce(st, { type: 'navPush', frame: frame('A', 'focus') });
		st = reduce(st, { type: 'navPush', frame: frame('B') });
		expect(st.navStack).toHaveLength(2);
		st = reduce(st, { type: 'navPop' });
		expect(topFrame(st.navStack)?.nodeId).toBe('A');
		st = reduce(st, { type: 'navClear' });
		expect(st.navStack).toEqual([]);
		st = reduce(st, { type: 'navPush', frame: frame('A') });
		st = reduce(st, { type: 'close' });
		expect(st.navStack).toEqual([]);
	});
});

describe('transcript windows around a referenced entry', () => {
	it('marks a window that does not reach the end as anchored, and a full tail as not', () => {
		const mid = replaceTranscript(result([entry(10), entry(11), entry(12)], 40), true);
		expect(mid.anchored).toBe(true);
		expect(mid.hasEarlier).toBe(true);
		const tail = replaceTranscript(result([entry(37), entry(38), entry(39)], 40), true);
		expect(tail.anchored).toBe(false);
		expect(replaceTranscript(result([entry(10)], 40)).anchored).toBe(false);
	});
	it('stops being anchored once "load later" reaches the end', () => {
		let t = replaceTranscript(result([entry(10), entry(11)], 15), true);
		t = appendEntries(t, result([entry(12), entry(13)], 15));
		expect(t.anchored).toBe(true);
		t = appendEntries(t, result([entry(14)], 15));
		expect(t.anchored).toBe(false);
	});
	it('finds an entry by id, falling back to the ordinal', () => {
		const es = [entry(10), entry(11)];
		expect(indexOfEntryId(es, 'f:11')).toBe(11);
		expect(indexOfEntryId([{ ...entry(11), entryId: undefined }], 'zz:11')).toBe(11);
		expect(indexOfEntryId(es, 'f:99')).toBe(-1);
	});
	it('the reducer stores the focus with an around window and clears it on a normal reload', () => {
		let st = initialState('tree');
		st = reduce(st, { type: 'openStart', id: 'n' });
		st = reduce(st, { type: 'transcriptAround', result: result([entry(10), entry(11)], 40), focus: { entryId: 'f:11', offset: 1, length: 2, mark: true, token: 1 } });
		expect(st.session?.focusEntry?.entryId).toBe('f:11');
		expect(st.session?.transcript.anchored).toBe(true);
		st = reduce(st, { type: 'transcriptReplace', result: result([entry(38), entry(39)], 40) });
		expect(st.session?.focusEntry).toBeNull();
		expect(st.session?.transcript.anchored).toBe(false);
	});
});

describe('file.read assembly', () => {
	const ctl = { cancelled: () => false, onProgress: () => {} };
	const chunk = (over: Partial<FileReadResult>): FileReadResult => ({ name: 'f', mime: 'text/plain', size: 0, offset: 0, length: 0, encoding: 'utf8', data: '', eof: false, ...over });

	it('concatenates text chunks until eof', async () => {
		const parts = ['Hel', 'lo ', 'wörld'];
		let i = 0;
		const read = async ({ offset }: { offset: number }) => {
			const data = parts[i++];
			return chunk({ offset, length: data.length, data, eof: i === parts.length });
		};
		expect(await readText(read, 'h', 12, 100, ctl)).toBe('Hello wörld');
	});
	it('decodes an svg that arrives as base64 into text', async () => {
		const b64 = btoa('<svg/>');
		const read = async () => chunk({ encoding: 'base64', data: b64, length: 6, eof: true });
		expect(await readText(read, 'h', 6, 100, ctl)).toBe('<svg/>');
	});
	it('assembles binary chunks into one blob of the requested type', async () => {
		const bytes = [new Uint8Array([1, 2, 3]), new Uint8Array([4, 5])];
		let i = 0;
		const read = async ({ offset }: { offset: number }) => {
			const b = bytes[i++];
			return chunk({ encoding: 'base64', data: btoa(String.fromCharCode(...b)), offset, length: b.length, eof: i === bytes.length });
		};
		const blob = await readBlob(read, 'h', 5, 100, 'image/png', ctl);
		expect(blob.size).toBe(5);
		expect(blob.type).toBe('image/png');
		expect([...base64ToBytes(btoa('AB'))]).toEqual([65, 66]);
	});
	it('can be cancelled, and refuses an endless or growing file', async () => {
		let n = 0;
		const read = async ({ offset }: { offset: number }) => chunk({ offset, length: 5, data: 'abcde', eof: false, size: 1000 });
		await expect(readText(read, 'h', 1000, 1000, { cancelled: () => ++n > 1, onProgress: () => {} })).rejects.toBeInstanceOf(ReadCancelled);
		await expect(readText(async () => chunk({ length: 0, eof: false }), 'h', 10, 100, ctl)).rejects.toThrow(/empty chunk/);
		await expect(readText(read, 'h', 10, 12, ctl)).rejects.toThrow(/grew/);
	});
});

describe('file markdown is sanitised strictly', () => {
	it('escapes raw html, drops images and script/javascript links', () => {
		const html = renderFileMarkdown('# T\n\n<script>alert(1)</script>\n\n<img src="https://t.example/p.png" onerror="x()">\n\n![alt text](https://t.example/p.png)\n\n[x](javascript:alert(1)) [ok](https://example.com) [rel](other.md) [hash](#frag)');
		expect(html).not.toMatch(/<script/i);
		expect(html).not.toMatch(/<img/i);
		expect(html).toContain('&lt;img'); // raw html is shown as inert text
		expect(html).not.toContain('javascript:');
		expect(html).not.toContain('href="other.md"');
		expect(html).not.toContain('href="#frag"');
		expect(html).toContain('href="https://example.com"');
		expect(html).toContain('rel="noopener noreferrer"');
		expect(html).toContain('target="_blank"');
		expect(html).toContain('alt text');
	});
	it('keeps tables, code blocks and headings', () => {
		const html = renderFileMarkdown('# H\n\n| a | b |\n|---|---|\n| 1 | 2 |\n\n```sql\nSELECT 1;\n```');
		expect(html).toContain('<h1');
		expect(html).toContain('class="tw"');
		expect(html).toContain('class="code"');
	});
});

describe('attachments', () => {
	it('checks the 200 MB cap and empty files', () => {
		expect(attachmentProblem({ name: 'a.png', size: 1024 })).toBeNull();
		expect(attachmentProblem({ name: 'a.png', size: MAX_ATTACHMENT_BYTES })).toBeNull();
		expect(attachmentProblem({ name: 'big.mov', size: MAX_ATTACHMENT_BYTES + 1 })).toMatch(/big\.mov.*limit/);
		expect(attachmentProblem({ name: 'e.txt', size: 0 })).toMatch(/empty/);
	});
	it('inserts the line at the caret, on its own line, and moves the caret below it', () => {
		const p = 'C:\\a\\b.png';
		expect(insertAttachedLine('', 0, 0, p)).toEqual({ text: `Attached file: ${p}\n`, caret: `Attached file: ${p}\n`.length });
		const mid = insertAttachedLine('look at this please', 12, 12, p);
		expect(mid.text).toBe(`look at this\nAttached file: ${p}\n please`);
		expect(mid.text.slice(0, mid.caret).endsWith('\n')).toBe(true);
		expect(insertAttachedLine('line one\n', 9, 9, p).text).toBe(`line one\nAttached file: ${p}\n`);
	});
	it('replaces a selection and clamps a bad caret', () => {
		expect(insertAttachedLine('abcdef', 2, 4, 'P').text).toBe('ab\nAttached file: P\nef');
		expect(insertAttachedLine('abc', 99, 120, 'P').text).toBe('abc\nAttached file: P\n');
	});
});
