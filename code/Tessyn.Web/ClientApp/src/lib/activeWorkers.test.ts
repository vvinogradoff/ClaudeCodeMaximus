import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import type { SessionStateDto } from '../api/types';
import { ActiveWorkersBadge } from '../components/SessionRow';
import { applySessionChanged, initialState } from '../state/appState';

const st = (extra: Partial<SessionStateDto> = {}): SessionStateDto => ({ id: 'a', running: false, streamingAlive: false, favorite: false, hasDraft: false, ...extra });
const html = (count?: number) => renderToStaticMarkup(createElement(ActiveWorkersBadge, { count }));

describe('active workers badge (FR.1.28)', () => {
	it('shows the number with the tooltip text', () => {
		const h = html(3);
		expect(h).toContain('>3<');
		expect(h).toContain('Workers running now: 3');
	});
	it('is absent at 0 and when the daemon omits the count', () => {
		expect(html(0)).toBe('');
		expect(html(undefined)).toBe('');
	});
	it('follows session.changed: the count rises, then clears when the daemon omits it', () => {
		let s = applySessionChanged(initialState('tree'), st({ runningWorkerCount: 2 }));
		expect(s.states.a.runningWorkerCount).toBe(2);
		s = applySessionChanged(s, st());
		expect(s.states.a.runningWorkerCount).toBeUndefined();
	});
});
