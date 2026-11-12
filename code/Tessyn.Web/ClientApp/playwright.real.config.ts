import { defineConfig } from '@playwright/test';

// Runs against the REAL Tessyn.Web (not the mock transport): `npm run build`, `dotnet build ..`, then `npm run test:e2e:real`.
export default defineConfig({
	testDir: './e2e-real',
	timeout: 60_000,
	workers: 1,
	reporter: 'list',
	use: {
		baseURL: 'http://127.0.0.1:5399',
		browserName: 'chromium',
		viewport: { width: 390, height: 844 },
		isMobile: true,
		hasTouch: true,
	},
	webServer: {
		command: 'node e2e-real/start-server.mjs',
		url: 'http://127.0.0.1:5399/healthz',
		reuseExistingServer: false,
		timeout: 60_000,
	},
});
