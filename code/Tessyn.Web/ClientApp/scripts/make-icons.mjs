// Renders public/icon.svg and public/icon-maskable.svg to the PNG sizes the PWA needs, using Playwright's chromium.
import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from '@playwright/test';

const pub = join(dirname(fileURLToPath(import.meta.url)), '..', 'public');
const jobs = [
	['icon.svg', 'icon-192.png', 192],
	['icon.svg', 'icon-512.png', 512],
	['icon.svg', 'apple-touch-icon.png', 180],
	['icon-maskable.svg', 'icon-maskable-512.png', 512],
];

const browser = await chromium.launch();
for (const [src, out, size] of jobs) {
	const svg = readFileSync(join(pub, src), 'utf8');
	const page = await browser.newPage({ viewport: { width: size, height: size } });
	await page.setContent(`<html><body style="margin:0;background:transparent">${svg.replace('<svg ', `<svg width="${size}" height="${size}" `)}</body></html>`);
	writeFileSync(join(pub, out), await page.screenshot({ omitBackground: true, type: 'png' }));
	await page.close();
	console.log('wrote', out);
}
await browser.close();
