// @vitest-environment jsdom
import { describe, expect, it } from 'vitest';
import { renderMarkdown } from './markdown';

describe('markdown sanitising', () => {
	it('strips scripts and event handlers', () => {
		const html = renderMarkdown('hi <script>alert(1)</script> <img src=x onerror="alert(2)"> **bold**');
		expect(html).not.toContain('<script');
		expect(html).not.toContain('onerror');
		expect(html).toContain('<strong>bold</strong>');
	});
	it('neutralises javascript: links', () => {
		const html = renderMarkdown('[x](javascript:alert(1))');
		expect(html).not.toContain('javascript:');
	});
	it('opens links in a new tab with noopener noreferrer', () => {
		const html = renderMarkdown('[docs](https://example.com)');
		expect(html).toContain('target="_blank"');
		expect(html).toContain('rel="noopener noreferrer"');
	});
	it('wraps tables for horizontal scroll and adds a copy button to code blocks', () => {
		const t = renderMarkdown('| a | b |\n|---|---|\n| 1 | 2 |');
		expect(t).toContain('<div class="tw"><table');
		const c = renderMarkdown('```ts\nconst x = 1;\n```');
		expect(c).toContain('class="code"');
		expect(c).toContain('data-copy');
		expect(c).toContain('<pre>');
	});
});
