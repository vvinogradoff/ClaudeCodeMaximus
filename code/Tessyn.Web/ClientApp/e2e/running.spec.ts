import { expect, test } from '@playwright/test';
import type { Page } from '@playwright/test';
import { expectNoHorizontalOverflow } from './helpers';

// The Running group of the Focus tab (FR.23.41, FR.23.42) against the mock backend: an approved elevated run is read-only, at the top, not an open card, not counted.

async function openFocus(page: Page) {
	await page.goto('/');
	if (await page.getByLabel('Email').waitFor({ timeout: 5000 }).then(() => true, () => false)) {
		await page.getByLabel('Email').fill('vitaly@example.com');
		await page.getByLabel('Password').fill('secret');
		await page.getByRole('button', { name: 'Sign in' }).click();
	}
	await expect(page.getByRole('button', { name: 'Sessions', exact: true })).toBeVisible();
	if ((await page.getByRole('button', { name: 'Sessions', exact: true }).getAttribute('aria-pressed')) !== 'true') await page.getByRole('button', { name: 'Sessions', exact: true }).click();
	await page.getByRole('tab', { name: 'Recent' }).click();
	await page.locator('.tab.active .srow', { hasText: 'Importer: schema drift' }).first().click();
	await page.getByRole('tab', { name: /^Focus/ }).click();
}

test.describe('focus running group', () => {
	test('a running elevated run sits on top, read-only, outside the open cards and the counts', async ({ page }, info) => {
		await openFocus(page);

		const group = page.getByTestId('running-group');
		await expect(group.getByTestId('running-header')).toHaveText('Running (1)');
		const card = group.getByTestId('running-card');
		await expect(card).toHaveCount(1);
		await expect(card).toContainText('Run the WPF E2E suite as the test user');
		await expect(card.getByTestId('elevated-dir')).toHaveText('Tessyn-wt-elevruns');
		await expect(card.locator('.chip-kind')).toHaveText('RUNNING');
		await expect(card.getByTestId('elevated-status')).toContainText('Running as administrator on the PC');
		await expect(card.getByTestId('running-approve-note')).toHaveText('Approve on the PC');
		await expect(card.getByRole('button')).toHaveCount(0);
		await expect(card.getByRole('textbox')).toHaveCount(0);

		// on top: above the first open card; not among the open cards
		const top = await group.boundingBox();
		const firstOpen = await page.getByTestId('focus-card').first().boundingBox();
		expect(top!.y).toBeLessThan(firstOpen!.y);
		await expect(page.getByTestId('focus-card').filter({ hasText: 'Run the WPF E2E suite' })).toHaveCount(0);

		// not counted: Send answers counts only the questionnaire (1), and the tab badge counts the two open asks, not the run
		await expect(page.getByRole('button', { name: /^Send answers \(1\)$/ })).toBeVisible();
		await expect(page.getByRole('tab', { name: /^Focus/ })).not.toContainText('3');
		await expectNoHorizontalOverflow(page, `${info.project.name} / running group`);
	});
});
