// @vitest-environment jsdom
import { renderToStaticMarkup } from 'react-dom/server';
import { createElement } from 'react';
import { describe, expect, it } from 'vitest';
import { inputLabel, isAutomatedInput } from './inputSource';
import { EntryView as Entry } from '../components/Transcript';
import type { SessionEntryDto } from '../api/types';

const user = (extra: Partial<SessionEntryDto> = {}): SessionEntryDto => ({ index: 1, timestamp: '2026-11-15T10:00:00Z', role: 'user', content: 'hello', ...extra });

describe('input source', () => {
	it('scheduled and orchestrated inputs are automated and labelled', () => {
		expect(isAutomatedInput('scheduled')).toBe(true);
		expect(isAutomatedInput('orchestrated')).toBe(true);
		expect(inputLabel('scheduled')).toBe('Scheduled');
		expect(inputLabel('orchestrated')).toBe('Supervisor');
	});

	it('the desktop user, the web and Telegram are the user\'s own', () => {
		for (const s of [undefined, 'web', 'telegram'] as const) {
			expect(isAutomatedInput(s)).toBe(false);
			expect(inputLabel(s)).toBe('You');
		}
	});
});

describe('user entry row', () => {
	const row = (e: SessionEntryDto) => renderToStaticMarkup(createElement(Entry, { e, now: Date.now(), onFull: () => {} }));

	it('a scheduled or supervisor input carries the auto class and its own label', () => {
		const scheduled = row(user({ source: 'scheduled' }));
		expect(scheduled).toContain('msg user auto');
		expect(scheduled).toContain('<span>Scheduled</span>');
		const supervisor = row(user({ source: 'orchestrated' }));
		expect(supervisor).toContain('msg user auto');
		expect(supervisor).toContain('<span>Supervisor</span>');
	});

	it('the user\'s own, web and Telegram inputs have no auto class', () => {
		for (const source of [undefined, 'web', 'telegram'] as const) {
			const html = row(user({ source }));
			expect(html).not.toContain('auto');
			expect(html).toContain('<span>You</span>');
		}
	});
});
