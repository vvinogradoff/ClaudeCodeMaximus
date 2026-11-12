import { expect, test } from '@playwright/test';

const EMAIL = 'e2e@example.test';
const PASSWORD = 'e2e-correct-horse-battery';

test('real server: login, desktop offline, logout really ends the session', async ({ page, request }) => {
	// Not logged in yet.
	expect((await request.get('/api/auth/me')).status()).toBe(401);

	await page.goto('/');
	await page.getByLabel('Email').fill(EMAIL);
	await page.getByLabel('Password').fill(PASSWORD);
	await page.getByRole('button', { name: 'Sign in' }).click();

	// Logged in (the cookie really works) ...
	await expect(page.getByRole('button', { name: 'Menu' })).toBeVisible();
	expect((await page.request.get('/api/auth/me')).status()).toBe(200);

	// ... and with no desktop linked the sessions panel shows the offline state (the server answers "Daemon offline").
	await page.getByRole('button', { name: 'Sessions', exact: true }).click();
	await expect(page.getByText('Daemon offline').first()).toBeVisible();

	// Log out through the UI: back at the login form, and the server no longer accepts the cookie.
	await page.getByRole('button', { name: 'Menu' }).click();
	await page.getByRole('menuitem', { name: 'Log out' }).click();
	await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible();
	expect((await page.request.get('/api/auth/me')).status()).toBe(401);
});
