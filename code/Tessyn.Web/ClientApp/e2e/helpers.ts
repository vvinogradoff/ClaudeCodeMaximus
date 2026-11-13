import { expect } from '@playwright/test';
import type { Page } from '@playwright/test';

/** Horizontal scrollers that are allowed to be wider than the viewport inside; their descendants are not checked. */
const SCROLLERS = '.sv-tags, .tw, .md .code pre';
/** Regions whose elements must never poke out of the viewport. */
const REGIONS = '.topbar, .sv-header, .transcript, .composer, .usage, .focus, .card, .panel, .sheet, .dialog, .toasts, .menu, .backdrop';

export interface OverflowReport { docScrollWidth: number; innerWidth: number; offenders: string[] }

export async function measureOverflow(page: Page): Promise<OverflowReport> {
	return page.evaluate(({ scrollers, regions }) => {
		const iw = window.innerWidth;
		const offenders: string[] = [];
		const describe = (el: Element) => `${el.tagName.toLowerCase()}${el.id ? '#' + el.id : ''}${typeof el.className === 'string' && el.className ? '.' + el.className.trim().split(/\s+/).join('.') : ''} "${(el.textContent ?? '').trim().slice(0, 30)}"`;
		for (const region of Array.from(document.querySelectorAll(regions))) {
			const all = [region, ...Array.from(region.querySelectorAll('*'))];
			for (const el of all) {
				if (el.closest(scrollers) && !el.matches(scrollers)) continue; // inside a horizontal scroller
				const cs = getComputedStyle(el);
				if (cs.display === 'none' || cs.visibility === 'hidden' || cs.position === 'fixed' && el.matches('.backdrop')) continue;
				const r = el.getBoundingClientRect();
				if (r.width === 0 && r.height === 0) continue;
				if (r.right > iw + 1 || r.left < -1) offenders.push(`${describe(el)} left=${r.left.toFixed(0)} right=${r.right.toFixed(0)}`);
			}
		}
		return { docScrollWidth: document.documentElement.scrollWidth, innerWidth: iw, offenders: Array.from(new Set(offenders)).slice(0, 12) };
	}, { scrollers: SCROLLERS, regions: REGIONS });
}

/** Document scrollWidth <= innerWidth and no region element beyond the viewport (outside the intentional scrollers). */
export async function expectNoHorizontalOverflow(page: Page, step = ''): Promise<void> {
	const r = await measureOverflow(page);
	expect(r.docScrollWidth, `${step}: document scrollWidth ${r.docScrollWidth} > innerWidth ${r.innerWidth}`).toBeLessThanOrEqual(r.innerWidth);
	expect(r.offenders, `${step}: elements outside the viewport`).toEqual([]);
}
