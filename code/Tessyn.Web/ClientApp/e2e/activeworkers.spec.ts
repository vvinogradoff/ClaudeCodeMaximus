import { expect, test } from '@playwright/test';
import type { Page } from '@playwright/test';
import { expectNoHorizontalOverflow } from './helpers';

// The active workers badge (FR.1.28) against the mock backend: the supervisor row shows how many workers run now, left of the "N workers" total; it follows the live state events.

async function openTree(page: Page) {
	await page.goto('/');
	if (await page.getByLabel('Email').waitFor({ timeout: 5000 }).then(() => true, () => false)) {
		await page.getByLabel('Email').fill('vitaly@example.com');
		await page.getByLabel('Password').fill('secret');
		await page.getByRole('button', { name: 'Sign in' }).click();
	}
	await expect(page.getByRole('button', { name: 'Sessions', exact: true })).toBeVisible();
	if ((await page.getByRole('button', { name: 'Sessions', exact: true }).getAttribute('aria-pressed')) !== 'true') await page.getByRole('button', { name: 'Sessions', exact: true }).click();
}

test.describe('active workers badge', () => {
	test('the supervisor row shows the running workers left of the total, in the tree and in Recent', async ({ page }, info) => {
		await openTree(page);

		const sup = page.locator('.tab.active .srow', { hasText: 'Orchestrate WPF rewrite' }).first();
		const badge = sup.getByTestId('active-workers');
		await expect(badge).toHaveText('2');
		await expect(badge).toHaveAttribute('title', 'Workers running now: 2');
		// left of the "N workers" total
		const total = await sup.getByRole('button', { name: '4 workers', exact: true }).boundingBox();
		const box = await badge.boundingBox();
		expect(box!.x + box!.width).toBeLessThanOrEqual(total!.x + 1);
		// a row without running workers shows none
		await expect(page.locator('.tab.active .srow', { hasText: 'Worker: tests' }).getByTestId('active-workers')).toHaveCount(0);
		await expectNoHorizontalOverflow(page, `${info.project.name} / active workers badge`);

		// recent: the same face
		await page.getByRole('tab', { name: 'Recent' }).click();
		await expect(page.locator('.tab.active .srow', { hasText: 'Orchestrate WPF rewrite' }).first().getByTestId('active-workers')).toHaveText('2');
		await expectNoHorizontalOverflow(page, `${info.project.name} / active workers badge in Recent`);
	});
});
