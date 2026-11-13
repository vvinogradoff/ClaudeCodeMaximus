import { expect, test } from '@playwright/test';
import type { Page } from '@playwright/test';
import { mkdirSync } from 'node:fs';
import { expectNoHorizontalOverflow } from './helpers';

const SHOTS = 'screenshots';
mkdirSync(SHOTS, { recursive: true });
const STRESS = 'HSE-225 portrait stress';

async function login(page: Page) {
	await page.goto('/');
	await page.getByLabel('Email').fill('vitaly@example.com');
	await page.getByLabel('Password').fill('secret');
	await page.getByRole('button', { name: 'Sign in' }).click();
	await expect(page.getByText('No session open')).toBeVisible();
}
const openPanel = (page: Page) => page.getByRole('button', { name: 'Sessions', exact: true }).click();
const row = (page: Page, text: string) => page.locator('.tab.active .srow', { hasText: text }).first();
async function openByRecent(page: Page, name: string) {
	if ((await page.getByRole('button', { name: 'Sessions', exact: true }).getAttribute('aria-pressed')) !== 'true') await openPanel(page);
	await page.getByRole('tab', { name: 'Recent' }).click();
	await row(page, name).click();
	await expect(page.locator('.topbar .title-btn')).toContainText(name.slice(0, 20));
}

test.describe('portrait: nothing is wider than the screen', () => {
	test('stress session, sheets, dialogs, tabs', async ({ page }, info) => {
		const prefix = info.project.name.startsWith('webkit') ? `portrait-${info.project.name}` : `portrait-${info.project.name}`;
		const shot = (name: string) => page.screenshot({ path: `${SHOTS}/${prefix}-${name}.png` });
		const check = (step: string) => expectNoHorizontalOverflow(page, `${info.project.name} / ${step}`);

		await login(page);
		await check('empty state');
		await page.getByRole('button', { name: 'Open sessions' }).click();
		await expect(page.getByTestId('tree-tab')).toBeVisible();
		await check('tree');
		await shot('tree');

		await openByRecent(page, STRESS);
		await check('recent opened session');
		await shot('recent-tab-then-session');

		// session with many tags + long URLs
		await expect(page.locator('.sv-tags .chip')).toHaveCount(8);
		await expect(page.locator('.msg.assistant').first()).toBeVisible();
		await check('stress transcript');
		await shot('session-many-tags');
		await page.locator('.md .tw').first().scrollIntoViewIfNeeded();
		await check('stress transcript table');
		await shot('long-url-transcript');

		// the tag row is a single scrollable row in portrait
		const tags = page.locator('.sv-tags');
		const box = await tags.evaluate((el) => ({ sw: el.scrollWidth, cw: el.clientWidth, h: el.getBoundingClientRect().height }));
		expect(box.sw).toBeGreaterThan(box.cw);
		expect(box.h).toBeLessThan(60);

		// focus
		await page.getByRole('tab', { name: /^Focus/ }).click();
		await expect(page.getByTestId('focus-card').first()).toBeVisible();
		await check('focus');
		await shot('focus');
		await page.getByTestId('focus-card').first().getByPlaceholder('Reply...').fill(`reply ${'z'.repeat(150)}`);
		await check('focus filled reply');

		// settings sheet
		await page.getByRole('tab', { name: /^Session/ }).click();
		await page.getByRole('button', { name: 'Session settings' }).click();
		await expect(page.getByTestId('settings-sheet')).toBeVisible();
		await check('settings sheet');
		await shot('settings-sheet');
		const sheet = await page.getByTestId('settings-sheet').boundingBox();
		expect(sheet!.x).toBeGreaterThanOrEqual(-1);
		expect(sheet!.x + sheet!.width).toBeLessThanOrEqual(page.viewportSize()!.width + 1);
		const dlg = page.getByRole('dialog', { name: 'Session settings' });
		await dlg.getByRole('combobox').nth(0).selectOption('claude-opus-5-5-1m');
		await dlg.getByRole('combobox').nth(1).selectOption('sys2');
		await check('settings sheet long options');
		await page.getByRole('button', { name: 'Done' }).click();

		// menu + compact confirm
		await page.getByRole('button', { name: 'Menu' }).click();
		await check('menu');
		await shot('menu');
		await page.getByRole('menuitem', { name: 'Compact session…' }).click();
		await expect(page.getByText('Compact this session?')).toBeVisible();
		await check('compact confirm');
		await page.getByRole('button', { name: 'Cancel' }).click();

		// recent / tags tabs, create dialog
		await openPanel(page);
		await check('panel over session');
		await page.getByRole('tab', { name: 'Recent' }).click();
		await check('recent');
		await shot('recent');
		await page.getByRole('tab', { name: 'Tags' }).click();
		await check('tags');
		await shot('tags');
		await page.getByRole('tab', { name: 'Tree' }).click();
		await shot('tree-2');
		await page.locator('.dir-row', { hasText: 'Tessyn' }).first().click();
		await page.getByRole('button', { name: /Create session/ }).click();
		await page.getByPlaceholder('Name').fill(`Dialog ${'n'.repeat(100)}`);
		await check('create dialog');
		await shot('create-dialog');
	});

	test('other long transcripts and the wide table', async ({ page }, info) => {
		await login(page);
		for (const name of ['Web client - SPA design', 'Wide table rendering test', 'Needs your decision']) {
			await openByRecent(page, name);
			await expect(page.locator('.msg').first()).toBeVisible();
			await expectNoHorizontalOverflow(page, `${info.project.name} / ${name}`);
			if (name === 'Needs your decision') {
				await page.getByRole('tab', { name: /^Focus/ }).click();
				await expectNoHorizontalOverflow(page, `${info.project.name} / ${name} focus`);
			}
		}
	});
});

test.describe('landscape reference', () => {
	test.use({ viewport: { width: 844, height: 390 } });
	test('screenshots at 844x390 (webkit only)', async ({ page }, info) => {
		test.skip(info.project.name !== 'webkit-390', 'one landscape run is enough');
		const tag = process.env.LANDSCAPE_TAG ?? 'after';
		const shot = (name: string) => page.screenshot({ path: `${SHOTS}/landscape-${tag}-${name}.png` });
		await login(page);
		await openByRecent(page, STRESS);
		await expect(page.locator('.sv-tags .chip')).toHaveCount(8);
		await shot('session-many-tags');
		await page.getByRole('tab', { name: /^Focus/ }).click();
		await expect(page.getByTestId('focus-card').first()).toBeVisible();
		await shot('focus');
		await page.getByRole('tab', { name: /^Session/ }).click();
		await page.getByRole('button', { name: 'Session settings' }).click();
		await expect(page.getByTestId('settings-sheet')).toBeVisible();
		await shot('settings-sheet');
		await page.mouse.click(20, 10); // backdrop: in landscape the sheet's Done button is below the fold
		await expect(page.getByTestId('settings-sheet')).toHaveCount(0);
		await openPanel(page);
		await shot('recent');
	});
});
