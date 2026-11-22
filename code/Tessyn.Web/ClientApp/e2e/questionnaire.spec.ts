import { expect, test } from '@playwright/test';
import type { Page } from '@playwright/test';
import { mkdirSync } from 'node:fs';
import { expectNoHorizontalOverflow } from './helpers';

// Focus questionnaires (FR.23.33-FR.23.37) against the mock backend. Runs on the phone-sized projects.

const SHOTS = 'screenshots';
mkdirSync(SHOTS, { recursive: true });

async function openQuestionnaire(page: Page) {
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
	const card = page.getByTestId('focus-card').filter({ hasText: 'Approve the schema migration plan' });
	await expect(card).toBeVisible();
	return card;
}

test.describe('focus questionnaire', () => {
	test('pre-filled from the recommendations; change, Reset, wrap on a phone, answers sent without a note', async ({ page }, info) => {
		const card = await openQuestionnaire(page);
		const q = (n: number) => card.getByTestId(`question-${n}`);

		// pre-filled, with the recommended marker; no Reset while unchanged
		await expect(q(1).getByRole('radio', { name: /No/ })).toHaveAttribute('aria-checked', 'true');
		await expect(q(2).getByRole('radio', { name: /A\s+Staging first/ })).toHaveAttribute('aria-checked', 'true');
		await expect(q(3).getByRole('checkbox', { name: /Products/ })).toHaveAttribute('aria-checked', 'true');
		await expect(q(3).getByRole('checkbox', { name: /Customers/ })).toHaveAttribute('aria-checked', 'false');
		await expect(q(4).getByRole('textbox')).toHaveValue('Keep the snapshot for 7 days.');
		await expect(q(1).getByText('recommended')).toBeVisible();
		await expect(q(5).getByRole('radio', { checked: true })).toHaveCount(0);
		await expect(card.getByRole('button', { name: 'Reset' })).toHaveCount(0);
		await expect(card.getByText('Note (optional)')).toBeVisible();
		await expectNoHorizontalOverflow(page, `${info.project.name} / questionnaire`);
		await page.screenshot({ path: `${SHOTS}/questionnaire-${info.project.name}.png` });

		// radio change, Reset restores, single clears on a second click, multi toggles independently
		await q(1).getByRole('radio', { name: /Yes/ }).click();
		await expect(q(1).getByRole('radio', { name: /No/ })).toHaveAttribute('aria-checked', 'false');
		await q(1).getByRole('button', { name: 'Reset' }).click();
		await expect(q(1).getByRole('radio', { name: /No/ })).toHaveAttribute('aria-checked', 'true');
		await q(2).getByRole('radio', { name: /A\s+Staging first/ }).click();
		await expect(q(2).getByRole('radio', { checked: true })).toHaveCount(0);
		await q(3).getByRole('checkbox', { name: /Customers/ }).click();
		await expect(q(3).getByRole('checkbox', { checked: true })).toHaveCount(3);
		await q(4).getByRole('textbox').fill('Something else');
		await expect(q(4).getByRole('button', { name: 'Reset' })).toBeVisible();

		// draft survives a reload (local storage, debounced)
		await page.waitForTimeout(600);
		await page.reload();
		const again = await openQuestionnaire(page);
		await expect(again.getByTestId('question-4').getByRole('textbox')).toHaveValue('Something else');

		// Reply is enabled with an empty note; the item closes with the answers as its outcome
		const reply = again.getByRole('button', { name: 'Reply', exact: true });
		await expect(reply).toBeEnabled();
		await again.getByTestId('question-4').getByRole('textbox').press('Control+Enter');
		await expect(page.getByTestId('focus-card').filter({ hasText: 'Approve the schema migration plan' })).toHaveCount(0);
		await page.getByRole('button', { name: /^›? ?Closed/ }).click();
		await expect(page.getByTestId('closed-card').filter({ hasText: 'Approve the schema migration plan' })).toContainText('Q4 Anything to add to the rollback plan?: Something else');
	});
});
