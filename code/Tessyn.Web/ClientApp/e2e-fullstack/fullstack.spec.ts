import { expect, test } from '@playwright/test';
import type { Page } from '@playwright/test';
import { mkdirSync } from 'node:fs';

// One long flow against the REAL stack (Tessyn.Web + RelayConnectorService + Core daemon seeded by DaemonHarness).
// Seed names come from Tessyn.Web.E2E.Tests/Harness/SeedModel.cs.
const EMAIL = process.env.TESSYN_EMAIL ?? 'vitaly@example.test';
const PASSWORD = process.env.TESSYN_PASSWORD ?? '';

const SHOTS = 'screenshots';
mkdirSync(SHOTS, { recursive: true });
const shot = (page: Page, name: string) => page.screenshot({ path: `${SHOTS}/fullstack-${name}.png` });

const openPanel = async (page: Page) => {
	if (await page.getByTestId('tree-tab').isVisible().catch(() => false)) return;
	await page.getByRole('button', { name: 'Sessions', exact: true }).click();
};
const row = (page: Page, text: string) => page.locator('.tab.active .srow', { hasText: text }).first();

// The daemon seeds directories collapsed (desktop default), so a directory is expanded by tapping it (this also selects it).
const expandDir = async (page: Page, dir: string, sample: string) => {
	if (await row(page, sample).waitFor({ state: 'visible', timeout: 1500 }).then(() => true, () => false)) return;
	await page.locator('.dir-row', { hasText: dir }).first().click();
	await expect(row(page, sample)).toBeVisible();
};

test('full stack: the SPA against the real relay, daemon and fake claude', async ({ page }) => {
	// Any page or console error after sign-in is a failure (the 401 of the anonymous session probe on first load is expected).
	const errors: string[] = [];
	let signedIn = false;
	page.on('pageerror', (e) => errors.push(`pageerror: ${e.message}`));
	page.on('console', (m) => { if (signedIn && m.type() === 'error') errors.push(`console: ${m.text()}`); });

	// ---- login
	await page.goto('/');
	await page.getByLabel('Email').fill(EMAIL);
	await page.getByLabel('Password').fill(PASSWORD);
	await page.getByRole('button', { name: 'Sign in' }).click();
	await expect(page.getByRole('button', { name: 'Menu' })).toBeVisible();
	await expect(page.locator('.link-dot.on')).toBeVisible();
	signedIn = true;
	await shot(page, '01-logged-in');

	// ---- tree: seeded names, supervisor workers
	await openPanel(page);
	await expect(page.getByTestId('tree-tab')).toBeVisible();
	for (const dir of ['alpha', 'beta'])
		await expect(page.locator('.dir-row', { hasText: dir }).first()).toBeVisible();
	await expandDir(page, 'alpha', 'Alpha supervisor');
	await expandDir(page, 'beta', 'Beta plain');
	await expect(row(page, 'Alpha supervisor')).toBeVisible();
	await expect(row(page, 'Alpha favourite')).toBeVisible();
	await expect(page.locator('.tab.active .srow', { hasText: 'Alpha worker one' })).toHaveCount(0);
	await shot(page, '02-tree');

	await page.getByRole('button', { name: '2 workers', exact: true }).click();
	await expect(row(page, 'Alpha worker one')).toBeVisible();
	await expect(row(page, 'Alpha worker two')).toBeVisible();
	await shot(page, '03-workers');

	// ---- search (real FTS index over a Claude transcript)
	const search = page.getByLabel('Search sessions');
	await search.fill('zebrafinch');
	await expect(row(page, 'Beta search target')).toBeVisible();
	await expect(page.locator('.tab.active .srow')).toHaveCount(1);
	await shot(page, '04-search');
	await page.getByRole('button', { name: 'Clear search' }).click();
	await expandDir(page, 'alpha', 'Alpha supervisor');

	// ---- create a session after an anchor (the beta directory) and open it
	await expandDir(page, 'beta', 'Beta plain');
	await page.locator('.dir-row', { hasText: 'beta' }).first().click();
	await page.getByRole('button', { name: /Create session/ }).click();
	await page.getByPlaceholder('Name').fill('Phone made session');
	await page.getByRole('button', { name: 'Create', exact: true }).click();
	await expect(page.getByTestId('session-view')).toBeVisible();
	await expect(page.locator('.topbar .title-btn')).toHaveText('Phone made session');
	await shot(page, '05-created');

	// ---- send a prompt, see the fake assistant reply render
	await page.getByPlaceholder('Message…').fill('Hello from the real stack');
	await page.getByRole('button', { name: 'Send', exact: true }).click();
	await expect(page.locator('.msg.user:not(.pending) .text', { hasText: 'Hello from the real stack' })).toBeVisible({ timeout: 30_000 });
	await expect(page.locator('.msg.assistant', { hasText: 'Fake reply' })).toBeVisible({ timeout: 30_000 });
	await expect(page.getByTestId('pending')).toHaveCount(0);
	await expect(page.getByTestId('thinking')).toHaveCount(0);
	await shot(page, '06-reply');

	// ---- usage strip (fake usage endpoint: 42 % / 17 %)
	await expect(page.getByTestId('usage-strip')).toBeVisible();
	await expect(page.getByTestId('usage-strip')).toContainText('5h: 42%');
	await expect(page.getByTestId('usage-strip')).toContainText('7d: 17%');

	// ---- Focus: reply to the ask, dismiss the todo
	await openPanel(page);
	await expandDir(page, 'alpha', 'Alpha supervisor');
	await row(page, 'Alpha supervisor').click();
	await expect(page.locator('.topbar .title-btn')).toHaveText('Alpha supervisor');
	await page.getByRole('tab', { name: /^Focus/ }).click();
	await expect(page.getByTestId('focus-card').first()).toBeVisible();
	await shot(page, '07-focus');
	const ask = page.getByTestId('focus-card').filter({ hasText: 'Which database should we use?' });
	await ask.getByRole('button', { name: 'Postgres', exact: true }).click();
	await expect(ask.getByPlaceholder('Reply...')).toHaveValue('Postgres');
	await ask.getByRole('button', { name: 'Reply', exact: true }).click();
	await expect(page.getByTestId('focus-card').filter({ hasText: 'Which database should we use?' })).toHaveCount(0);
	const todo = page.getByTestId('focus-card').filter({ hasText: 'Rotate the staging key' });
	await todo.getByRole('button', { name: 'Dismiss' }).click();
	await expect(page.getByTestId('focus-card').filter({ hasText: 'Rotate the staging key' })).toHaveCount(0);
	await page.getByRole('button', { name: /^›? ?Closed/ }).click();
	await expect(page.getByTestId('closed-card').first()).toBeVisible();
	await expect(page.getByTestId('closed-card').filter({ hasText: 'Answered: Postgres' })).toBeVisible();
	await shot(page, '08-focus-after');

	// the reply started a real turn: the fake assistant answered in the supervisor's transcript
	await page.getByRole('tab', { name: /^Session/ }).click();
	await expect(page.locator('.msg.assistant', { hasText: 'Fake reply' }).last()).toBeVisible({ timeout: 30_000 });

	// ---- Settings: favourite toggle, then Recent shows it pinned
	await openPanel(page);
	await expandDir(page, 'beta', 'Beta plain');
	await row(page, 'Beta plain').click();
	await expect(page.locator('.topbar .title-btn')).toHaveText('Beta plain');
	await page.getByRole('button', { name: 'Session settings' }).click();
	const dialog = page.getByRole('dialog', { name: 'Session settings' });
	await dialog.getByRole('checkbox').first().check();
	await expect(dialog.getByRole('checkbox').first()).toBeChecked();
	await shot(page, '09-settings');
	await page.getByRole('button', { name: 'Done' }).click();
	await openPanel(page);
	await page.getByRole('tab', { name: 'Recent' }).click();
	await expect(row(page, 'Beta plain')).toBeVisible();
	await shot(page, '10-recent');

	// ---- Tags: select a chip, sessions of that tag appear
	await page.getByRole('tab', { name: 'Tags' }).click();
	await expect(page.getByText('Select tags to list their sessions')).toBeVisible();
	// With a session open the chip list is scoped to its directory (only beta's tag use); drop the scope to see both directories.
	await page.getByRole('checkbox', { name: /Directory/ }).uncheck();
	await page.getByRole('button', { name: /widgets PR #12/ }).click();
	await expect(row(page, 'Backend api')).toBeVisible();
	await expect(row(page, 'Beta plain')).toBeVisible();
	await shot(page, '11-tags');

	// ---- Compact the session through the menu
	await row(page, 'Beta plain').click();
	await expect(page.locator('.topbar .title-btn')).toHaveText('Beta plain');
	await page.getByRole('button', { name: 'Menu' }).click();
	await page.getByRole('menuitem', { name: 'Compact session…' }).click();
	await page.getByRole('button', { name: 'Compact', exact: true }).click();
	await expect(page.getByText('Summary of the conversation so far')).toBeVisible({ timeout: 30_000 });
	await expect(page.locator('.compaction').first()).toBeVisible();
	await shot(page, '12-compacted');

	expect(errors, errors.join('\n')).toEqual([]);
});
