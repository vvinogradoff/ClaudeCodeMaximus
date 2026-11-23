import { expect, test } from '@playwright/test';
import type { Page } from '@playwright/test';
import { mkdirSync } from 'node:fs';

const SHOTS = 'screenshots';
mkdirSync(SHOTS, { recursive: true });
const shot = (page: Page, name: string) => page.screenshot({ path: `${SHOTS}/${name}.png` });

async function login(page: Page) {
	await page.goto('/');
	await page.getByLabel('Email').fill('vitaly@example.com');
	await page.getByLabel('Password').fill('secret');
	await page.getByRole('button', { name: 'Sign in' }).click();
	await expect(page.getByText('No session open')).toBeVisible();
}

const openPanel = (page: Page) => page.getByRole('button', { name: 'Sessions', exact: true }).click();
const row = (page: Page, text: string) => page.locator('.tab.active .srow', { hasText: text }).first();

test.describe('mobile flows', () => {
	test('login → tree → search → create → send → focus → settings → recent → tags', async ({ page }) => {
		// ---- login (error, then success)
		await page.goto('/');
		await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible();
		await shot(page, '01-login');
		await page.getByLabel('Email').fill('vitaly@example.com');
		await page.getByLabel('Password').fill('wrong');
		await page.getByRole('button', { name: 'Sign in' }).click();
		await expect(page.getByRole('alert')).toContainText('Invalid email or password');
		await shot(page, '02-login-error');
		await page.getByLabel('Password').fill('secret');
		await page.getByRole('button', { name: 'Sign in' }).click();
		await expect(page.getByText('No session open')).toBeVisible();
		await shot(page, '03-empty-state');

		// ---- tree
		await page.getByRole('button', { name: 'Open sessions' }).click();
		await expect(page.getByTestId('tree-tab')).toBeVisible();
		await expect(row(page, 'Orchestrate WPF rewrite')).toBeVisible();
		await expect(page.locator('.srow.rec-strong').first()).toBeVisible();
		await shot(page, '04-tree');

		// supervisor workers expand lazily
		await page.getByRole('button', { name: '4 workers', exact: true }).click();
		await expect(row(page, 'Worker: models')).toBeVisible();
		await shot(page, '05-tree-workers');

		// ---- search
		const search = page.getByLabel('Search sessions');
		await search.fill('retry');
		await expect(page.locator('.tab.active .srow', { hasText: 'Relay connector backoff' })).toHaveCount(0);
		await expect(row(page, 'Importer: retry on 429')).toBeVisible();
		await expect(page.locator('.tab.active .srow')).toHaveCount(1);
		await shot(page, '06-search');
		await page.getByRole('button', { name: 'Clear search' }).click();
		await expect(row(page, 'Relay connector backoff')).toBeVisible();

		// ---- create session (anchor = selected directory)
		await page.locator('.dir-row', { hasText: 'Tessyn' }).first().click();
		await page.getByRole('button', { name: /Create session/ }).click();
		await page.getByPlaceholder('Name').fill('Phone test session');
		await shot(page, '07-create-dialog');
		await page.getByRole('button', { name: 'Create', exact: true }).click();
		await expect(page.getByTestId('session-view')).toBeVisible();
		await expect(page.locator('.topbar .title-btn')).toHaveText('Phone test session');
		await expect(page.getByPlaceholder('Message…')).toBeFocused();
		await shot(page, '08-new-session');

		// ---- send → pending → thinking → reply
		await page.getByPlaceholder('Message…').fill('Hello from the phone');
		await page.getByRole('button', { name: 'Send', exact: true }).click();
		await expect(page.getByTestId('pending')).toBeVisible();
		await shot(page, '09-pending');
		await expect(page.getByTestId('thinking')).toBeVisible();
		await shot(page, '10-thinking');
		await expect(page.locator('.msg.user:not(.pending) .text', { hasText: 'Hello from the phone' })).toBeVisible();
		await expect(page.getByTestId('pending')).toHaveCount(0);
		await expect(page.locator('.msg.assistant', { hasText: 'Got it' })).toBeVisible({ timeout: 10_000 });
		await expect(page.getByTestId('thinking')).toHaveCount(0);
		await shot(page, '11-reply');

		// ---- long transcript (via Recent tab)
		await openPanel(page);
		await page.getByRole('tab', { name: 'Recent' }).click();
		await expect(row(page, 'Web client - SPA design')).toBeVisible();
		await expect(page.locator('.tab.active .hide-btn').first()).toBeVisible();
		await shot(page, '12-recent');
		await row(page, 'Web client - SPA design').click();
		await expect(page.locator('.topbar .title-btn')).toHaveText('Web client - SPA design');
		await expect(page.locator('.msg.assistant').first()).toBeVisible();
		await expect(page.locator('.compaction').first()).toBeVisible();
		await expect(page.getByTestId('usage-strip')).toBeVisible();
		await shot(page, '13-transcript');
		await page.getByRole('button', { name: 'Load earlier' }).click();
		await expect(page.locator('.transcript .msg').first()).toBeVisible();
		await page.locator('.md table').first().scrollIntoViewIfNeeded();
		await shot(page, '14-transcript-markdown');

		// ---- focus: reply and dismiss
		await openPanel(page);
		await page.getByRole('tab', { name: 'Tree' }).click();
		await row(page, 'Needs your decision').click();
		await expect(page.locator('.topbar .title-btn')).toHaveText('Needs your decision');
		await expect(page.locator('.seg .pill')).toBeVisible();
		await shot(page, '15-session-with-focus-badge');
		await page.getByRole('tab', { name: /^Focus/ }).click();
		await expect(page.getByTestId('focus-card').first()).toBeVisible();
		await shot(page, '16-focus');
		const askCard = page.getByTestId('focus-card').filter({ hasText: 'Which retry policy' });
		await askCard.getByRole('button', { name: 'Linear backoff' }).click();
		await expect(askCard.getByPlaceholder('Reply...')).toHaveValue('Linear backoff');
		await askCard.getByRole('button', { name: /Use recommendation/ }).click();
		await expect(askCard.getByPlaceholder('Reply...')).toHaveValue('Linear backoff\nAccepted your recommendation: Exponential backoff');
		await shot(page, '17-focus-reply-filled');
		await askCard.getByRole('button', { name: 'Reply', exact: true }).click();
		await expect(page.getByTestId('focus-card').filter({ hasText: 'Which retry policy' })).toHaveCount(0);
		const critical = page.getByTestId('focus-card').filter({ hasText: 'Production database migration' });
		await critical.getByRole('button', { name: 'Got it' }).click();
		await expect(critical).toHaveCount(0);
		await page.getByRole('button', { name: /^›? ?Closed/ }).click();
		await expect(page.getByTestId('closed-card').first()).toBeVisible();
		await shot(page, '18-focus-after');

		// ---- settings sheet
		await page.getByRole('tab', { name: /^Session/ }).click();
		await page.getByRole('button', { name: 'Session settings' }).click();
		await expect(page.getByTestId('settings-sheet')).toBeVisible();
		await page.getByRole('dialog', { name: 'Session settings' }).getByRole('combobox').nth(0).selectOption('claude-sonnet-5-5');
		await shot(page, '19-settings');
		await page.getByRole('button', { name: 'Done' }).click();

		// ---- tags
		await openPanel(page);
		await page.getByRole('tab', { name: 'Tags' }).click();
		await expect(page.getByText('Select tags to list their sessions')).toBeVisible();
		await shot(page, '20-tags');
		await page.getByRole('button', { name: /TES-142/ }).click();
		await expect(page.locator('.tab.active .srow', { hasText: 'Relay connector backoff' })).toBeVisible();
		await shot(page, '21-tags-selected');
	});

	// TC.CX.42 (FR.33.2, FR.33.3, FR.33.17, FR.25.24): the pickers follow the model's engine; Codex settings are read-only on the web.
	test('GPT session: engine-aware pickers, read-only Codex status, labelled usage, send', async ({ page }) => {
		await login(page);
		await openPanel(page);
		await row(page, 'GPT session: tidy the changelog').click();
		await expect(page.locator('.topbar .title-btn')).toHaveText('GPT session: tidy the changelog');

		// usage: the ChatGPT profile's windows, the exhausted one marked
		await expect(page.getByTestId('usage-strip')).toBeVisible();
		await expect(page.locator('.usage-bar.exhausted')).toHaveCount(1);
		await expect(page.locator('.usage-bar.exhausted')).toContainText('5h: 100%');
		await expect(page.locator('.usage-bar.exhausted')).toHaveAttribute('title', /out of credits/);

		await page.getByRole('button', { name: 'Session settings' }).click();
		const sheet = page.getByTestId('settings-sheet');
		await expect(sheet).toBeVisible();
		const model = page.getByTestId('model-select');
		const effort = page.getByTestId('effort-select');
		const profile = page.getByTestId('profile-select');
		await expect(model).toHaveValue('gpt-6.2-sol');
		await expect(model.locator('optgroup')).toHaveCount(3);
		await expect(model.locator('optgroup[label="GPT (ChatGPT plan)"] option').first()).toHaveText('GPT-6.2-Sol'); // Codex's default first
		await expect(effort).toHaveValue('xhigh');
		await expect(effort.locator('option')).toHaveText(['Default', 'Low', 'Medium', 'High', 'Xhigh']);
		await expect(profile).toHaveValue('codex_1');
		await expect(profile.locator('option')).toHaveCount(1);
		await expect(sheet.getByText('Streaming', { exact: true })).toHaveCount(0);
		await expect(page.getByTestId('codex-status')).toContainText('Codex 0.172.0 found');
		await expect(page.getByTestId('codex-status')).toContainText('newer than the tested');
		await expect(page.getByTestId('codex-desktop-note')).toHaveText(/Sign in to ChatGPT in the desktop app/);
		await shot(page, '24-settings-gpt');

		// a Claude model: the Claude efforts and profiles, Streaming back; the GPT-only effort shows as Default
		await model.selectOption('claude-opus-5-5');
		await expect(effort.locator('option')).toHaveText(['Default', 'Max', 'High', 'Medium', 'Low']);
		await expect(effort).toHaveValue('default');
		await expect(profile).toHaveValue('default');
		await expect(profile.locator('option')).toHaveCount(5);
		await expect(sheet.getByText('Streaming', { exact: true })).toBeVisible();

		// back to GPT: the stored effort and profile return
		await model.selectOption('gpt-6.2-sol');
		await expect(effort).toHaveValue('xhigh');
		await expect(profile).toHaveValue('codex_1');
		await effort.selectOption('high');
		await page.getByRole('button', { name: 'Done' }).click();

		// a send from the GPT session carries the choice; the reply is stamped with it
		await page.getByPlaceholder('Message…').fill('Hello GPT');
		await page.getByRole('button', { name: 'Send', exact: true }).click();
		await expect(page.locator('.msg.assistant', { hasText: 'Got it - working on: Hello GPT' })).toBeVisible({ timeout: 10_000 });
	});

	test('offline banner, compact, and logout', async ({ page }) => {
		await login(page);
		await openPanel(page);
		await row(page, 'Web client - SPA design').click();
		await expect(page.locator('.topbar .title-btn')).toHaveText('Web client - SPA design');
		await page.evaluate(() => (window as unknown as { __mock: { setOnline(v: boolean): void } }).__mock.setOnline(false));
		await expect(page.getByText('Desktop offline')).toBeVisible();
		await expect(page.locator('.link-dot.off')).toBeVisible();
		await shot(page, '22-offline');
		await page.evaluate(() => (window as unknown as { __mock: { setOnline(v: boolean): void } }).__mock.setOnline(true));
		await expect(page.getByText('Desktop offline')).toHaveCount(0);

		await page.getByRole('button', { name: 'Menu' }).click();
		await page.getByRole('menuitem', { name: 'Compact session…' }).click();
		await expect(page.getByText('Compact this session? The transcript is replaced by a summary.')).toBeVisible();
		await page.getByRole('button', { name: 'Compact', exact: true }).click();
		await expect(page.getByText('Summary of the conversation so far')).toBeVisible();

		await page.getByRole('button', { name: 'Menu' }).click();
		await page.getByRole('menuitem', { name: 'Log out' }).click();
		await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible();
	});
});

test.describe('desktop width', () => {
	test.use({ viewport: { width: 1280, height: 800 }, deviceScaleFactor: 1, isMobile: false, hasTouch: false });
	test('main screen at 1280x800', async ({ page }) => {
		await login(page);
		await openPanel(page);
		await row(page, 'Web client - SPA design').click();
		await expect(page.locator('.msg.assistant').first()).toBeVisible();
		await shot(page, '23-desktop-1280x800');
	});
});
