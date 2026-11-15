import { defineConfig } from '@playwright/test';

const mobile = { deviceScaleFactor: 2, isMobile: true, hasTouch: true };

export default defineConfig({
	testDir: './e2e',
	timeout: 60_000,
	workers: 1,
	reporter: 'list',
	use: { baseURL: 'http://127.0.0.1:4173' },
	projects: [
		// the original flows (screenshots 01-23) run on chromium 390x844 only
		{ name: 'chromium-390', testMatch: /(flows|portrait|refs)\.spec\.ts/, use: { browserName: 'chromium', viewport: { width: 390, height: 844 }, ...mobile } },
		// iOS Safari is WebKit: the portrait overflow only reproduces there
		{ name: 'webkit-390', testMatch: /(portrait|refs)\.spec\.ts/, use: { browserName: 'webkit', viewport: { width: 390, height: 844 }, ...mobile } },
		{ name: 'webkit-375', testMatch: /(portrait|refs)\.spec\.ts/, use: { browserName: 'webkit', viewport: { width: 375, height: 667 }, ...mobile } },
	],
	webServer: {
		command: 'npx vite preview --mode mock --host 127.0.0.1 --port 4173 --strictPort',
		url: 'http://127.0.0.1:4173',
		reuseExistingServer: true,
		timeout: 30_000,
	},
});
