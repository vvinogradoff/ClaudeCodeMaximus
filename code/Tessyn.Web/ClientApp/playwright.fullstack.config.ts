import { defineConfig } from '@playwright/test';

// Runs the SPA against the FULL stack: real Tessyn.Web, real RelayConnectorService, real Core daemon (fake claude process).
// The stack is started by the xUnit test Tessyn.Web.E2E.Tests (FullStackTests, TESSYN_FULLSTACK=1), which passes the
// URL and credentials in TESSYN_URL / TESSYN_EMAIL / TESSYN_PASSWORD. There is no webServer here on purpose.
export default defineConfig({
	testDir: './e2e-fullstack',
	timeout: 180_000,
	workers: 1,
	reporter: 'list',
	use: {
		actionTimeout: 10_000,
		navigationTimeout: 15_000,
		baseURL: process.env.TESSYN_URL ?? 'http://127.0.0.1:5398',
		browserName: 'chromium',
		viewport: { width: 390, height: 844 },
		deviceScaleFactor: 2,
		isMobile: true,
		hasTouch: true,
	},
});
