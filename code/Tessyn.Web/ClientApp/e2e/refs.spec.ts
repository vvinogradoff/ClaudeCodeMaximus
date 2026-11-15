import { expect, test } from '@playwright/test';
import type { Page, TestInfo } from '@playwright/test';
import { mkdirSync } from 'node:fs';
import { expectNoHorizontalOverflow } from './helpers';
import { gradientPng } from './png';

// Focus references, file previews, transcript previews and attachments (mock backend). Runs on chromium-390 and the webkit portrait projects.

const SHOTS = 'screenshots';
mkdirSync(SHOTS, { recursive: true });

// a real (small) PNG, served for the https image links of the mock transcript
const PNG = gradientPng(320, 180);

async function login(page: Page) {
	await page.route('https://annielartens.online/**', (r) => r.fulfill({ status: 200, contentType: 'image/png', body: PNG }));
	await page.route('https://cdn.example.org/**', (r) => r.fulfill({ status: 404, body: 'nope' }));
	await page.goto('/');
	await page.getByLabel('Email').fill('vitaly@example.com');
	await page.getByLabel('Password').fill('secret');
	await page.getByRole('button', { name: 'Sign in' }).click();
	await expect(page.getByText('No session open')).toBeVisible();
}

async function openByRecent(page: Page, name: string) {
	if ((await page.getByRole('button', { name: 'Sessions', exact: true }).getAttribute('aria-pressed')) !== 'true') await page.getByRole('button', { name: 'Sessions', exact: true }).click();
	await page.getByRole('tab', { name: 'Recent' }).click();
	await page.locator('.tab.active .srow', { hasText: name }).first().click();
	await expect(page.locator('.topbar .title-btn')).toContainText(name.slice(0, 20));
}

const focusTab = (page: Page) => page.getByRole('tab', { name: /^Focus/ }).click();
const sessionTab = (page: Page) => page.getByRole('tab', { name: /^Session/ }).click();
const check = (page: Page, info: TestInfo, step: string) => expectNoHorizontalOverflow(page, `${info.project.name} / ${step}`);
const shotter = (page: Page, info: TestInfo) => (name: string) => page.screenshot({ path: `${SHOTS}/refs-${info.project.name}-${name}.png` });

/** Is the highlighted range registered, and what text does it cover? */
const highlightedText = (page: Page) => page.evaluate(() => {
	const h = (CSS as unknown as { highlights?: Map<string, Iterable<Range>> }).highlights?.get('ref-hit');
	return h ? [...h].map((r) => r.toString()).join('|') : null;
});

const inView = (page: Page, selector: string) => page.evaluate((sel) => {
	const el = document.querySelector(sel); const box = document.querySelector('[data-testid="transcript"]');
	if (!el || !box) return false;
	const a = el.getBoundingClientRect(); const b = box.getBoundingClientRect();
	return a.top >= b.top - 1 && a.top < b.bottom - 20;
}, selector);

test.describe('focus references', () => {
	test('entry ref: jump, highlight, Back; nested jumps unwind step by step', async ({ page }, info) => {
		const shot = shotter(page, info);
		await login(page);

		// ---- Importer -> (Focus ref) -> "Needs your decision" -> (Focus ref) -> SPA session
		await openByRecent(page, 'Importer: schema drift');
		await focusTab(page);
		const card = page.getByTestId('focus-card').first();
		await expect(card.getByTestId('ref-entry')).toHaveText(/Affected tables \(other session\)/);
		await shot('01-focus-card-refs-importer');
		await card.getByTestId('ref-entry').click();

		await expect(page.locator('.topbar .title-btn')).toContainText('Needs your decision');
		await expect(page.getByTestId('back-bar')).toContainText('Back to Importer: schema drift · Focus');
		await expect(page.locator('.hl-entry')).toBeVisible();
		await expect.poll(() => highlightedText(page)).toBe('Migration touches exactly 3 tables');
		expect(await inView(page, '.hl-entry')).toBe(true);
		await check(page, info, 'after jump (cross-session)');
		await shot('02-jumped-highlight-back-bar');

		await focusTab(page);
		const critical = page.getByTestId('focus-card').filter({ hasText: 'Production database migration is blocked' });
		await expect(critical.getByTestId('ref-file')).toHaveCount(4);
		await expect(critical.getByTestId('ref-url')).toHaveAttribute('href', 'https://example.com/runbook');
		await expect(critical.getByTestId('ref-url')).toHaveAttribute('target', '_blank');
		await check(page, info, 'focus card with 6 refs');
		await shot('03-focus-card-refs-all-kinds');

		const retry = page.getByTestId('focus-card').filter({ hasText: 'Which retry policy' });
		await retry.getByTestId('ref-entry').click();
		await expect(page.getByTestId('back-bar')).toContainText('Back to Needs your decision · Focus');
		await expect(page.getByTestId('back-bar').locator('.back-depth')).toHaveText('2');
		await expect.poll(() => highlightedText(page)).toBe('typed JSON-RPC client plus an SSE event stream');
		await expect(page.getByTestId('anchored-bar')).toBeVisible(); // a window around the entry, not the latest page
		await check(page, info, 'after nested jump');
		await shot('04-nested-jump-anchored');

		await page.getByTestId('anchored-bar').getByRole('button', { name: 'Jump to latest' }).click();
		await expect(page.getByTestId('anchored-bar')).toHaveCount(0);
		await expect(page.getByText('Only the Playwright flows')).toBeVisible();

		// Back, Back
		await page.getByTestId('back-bar').click();
		await expect(page.locator('.topbar .title-btn')).toContainText('Needs your decision');
		await expect(page.getByTestId('focus')).toBeVisible();
		await expect(page.getByTestId('back-bar')).toContainText('Back to Importer: schema drift · Focus');
		await page.getByTestId('back-bar').click();
		await expect(page.locator('.topbar .title-btn')).toContainText('Importer: schema drift');
		await expect(page.getByTestId('focus')).toBeVisible();
		await expect(page.getByTestId('back-bar')).toHaveCount(0);
		await check(page, info, 'after Back x2');
	});

	test('same-session jump scrolls to the entry; Back returns to the Focus tab', async ({ page }) => {
		await login(page);
		await openByRecent(page, 'Needs your decision');
		await focusTab(page);
		await page.getByTestId('focus-card').filter({ hasText: 'Production database migration is blocked' }).getByTestId('ref-entry').click();
		await expect(page.getByTestId('transcript')).toBeVisible();
		await expect(page.getByTestId('back-bar')).toContainText('Back to Needs your decision · Focus');
		await expect.poll(() => highlightedText(page)).toBe('Migration touches exactly 3 tables');
		await page.getByTestId('back-bar').click();
		await expect(page.getByTestId('focus')).toBeVisible();
		await expect(page.getByTestId('back-bar')).toHaveCount(0);
		await sessionTab(page);
		await expect(page.getByTestId('transcript')).toBeVisible();
	});
});

test.describe('file preview', () => {
	test('markdown is rendered safely; source toggle; Escape closes; focus returns', async ({ page }, info) => {
		const shot = shotter(page, info);
		await login(page);
		await openByRecent(page, 'Needs your decision');
		await focusTab(page);
		const critical = page.getByTestId('focus-card').filter({ hasText: 'Production database migration is blocked' });
		const planBtn = critical.getByRole('button', { name: 'Preview file: Migration plan' });
		await planBtn.click();

		const sheet = page.getByTestId('preview-sheet');
		await expect(sheet).toBeVisible();
		await expect(sheet.getByRole('heading', { name: 'Migration plan' })).toBeVisible();
		const md = page.getByTestId('preview-md');
		await expect(md.locator('h1')).toHaveText('Migration plan');
		await expect(md.locator('table')).toHaveCount(1);
		await expect(md.locator('script, img, iframe')).toHaveCount(0);
		await expect(md.locator('a[href^="javascript"]')).toHaveCount(0);
		await expect(md.locator('a[href="https://example.com/runbook"]')).toHaveAttribute('rel', 'noopener noreferrer');
		await expect(md).toContainText('<script>alert'); // raw html is shown as inert text
		await check(page, info, 'markdown preview');
		await shot('05-preview-markdown');

		await sheet.getByRole('button', { name: 'Source' }).click();
		await expect(page.getByTestId('preview-text')).toContainText('# Migration plan');
		await page.keyboard.press('Escape');
		await expect(sheet).toHaveCount(0);
		await expect(planBtn).toBeFocused();
	});

	test('image through a blob URL; video; pdf as a download; too large', async ({ page }, info) => {
		const shot = shotter(page, info);
		await login(page);
		await openByRecent(page, 'Needs your decision');
		await focusTab(page);
		const critical = page.getByTestId('focus-card').filter({ hasText: 'Production database migration is blocked' });
		const sheet = page.getByTestId('preview-sheet');

		await critical.getByRole('button', { name: 'Preview file: photo.png' }).click();
		const img = sheet.locator('img.pv-img');
		await expect(img).toBeVisible();
		expect(await img.evaluate((el: HTMLImageElement) => el.naturalWidth)).toBeGreaterThan(100);
		expect(await img.getAttribute('src')).toMatch(/^blob:/);
		await check(page, info, 'image preview');
		await shot('06-preview-image');
		await sheet.getByRole('button', { name: 'Close preview' }).click();

		await critical.getByRole('button', { name: 'Preview file: Report (PDF)' }).click();
		await expect(sheet.getByRole('button', { name: 'Load PDF' })).toBeVisible();
		await sheet.getByRole('button', { name: 'Load PDF' }).click();
		await expect(sheet.getByRole('link', { name: 'Open in new tab' })).toHaveAttribute('target', '_blank');
		await expect(sheet.getByRole('link', { name: 'Download' })).toHaveAttribute('download', 'report.pdf');
		await shot('07-preview-pdf');
		await sheet.getByRole('button', { name: 'Close preview' }).click();

		await critical.getByRole('button', { name: 'Preview file: Full recording' }).click();
		await expect(page.getByTestId('preview-too-large')).toContainText('Too large');
		await expect(page.getByTestId('preview-too-large')).toContainText('320 MB');
		await check(page, info, 'too large');
		await shot('08-preview-too-large');
	});

	test('a path chip in an assistant entry previews through file.preview; svg and html are text', async ({ page }, info) => {
		const shot = shotter(page, info);
		await login(page);
		await openByRecent(page, 'Needs your decision');
		const chips = page.getByTestId('path-chip');
		await expect(chips.first()).toBeVisible();
		await chips.first().scrollIntoViewIfNeeded();
		await check(page, info, 'path chips');
		await shot('09-transcript-previews-and-chips');
		const sheet = page.getByTestId('preview-sheet');

		await page.getByRole('button', { name: 'Preview file: plan.md' }).click();
		await expect(page.getByTestId('preview-md').locator('h1')).toHaveText('Migration plan');
		await sheet.getByRole('button', { name: 'Close preview' }).click();

		await page.getByRole('button', { name: 'Preview file: notes.txt' }).click(); // multi-chunk text
		await expect(page.getByTestId('preview-text')).toContainText('Release notes - draft');
		await expect(page.getByTestId('preview-text')).toContainText('ünïcödé ✓ 日本語');
		await shot('10-preview-text');
		await sheet.getByRole('button', { name: 'Close preview' }).click();

		await page.getByRole('button', { name: 'Preview file: Greeter.cs' }).click(); // c:/temp/Greeter.cs: forward slashes + case
		await expect(page.getByTestId('preview-text')).toContainText('public static class Greeter');
		await sheet.getByRole('button', { name: 'Close preview' }).click();

		await page.getByRole('button', { name: 'Preview file: diagram.svg' }).click();
		await expect(page.getByTestId('preview-text')).toContainText('<svg');
		await expect(sheet.locator('svg, script, img')).toHaveCount(0);
		await sheet.getByRole('button', { name: 'Close preview' }).click();

		await page.getByRole('button', { name: 'Preview file: report.html' }).click();
		await expect(page.getByTestId('preview-text')).toContainText('<h1>Report</h1>');
		await expect(sheet.locator('h1, script, iframe')).toHaveCount(0);
		await sheet.getByRole('button', { name: 'Close preview' }).click();

		await page.getByRole('button', { name: 'Preview file: clip.mp4' }).click();
		await expect(sheet.getByRole('link', { name: 'Download' })).toBeVisible(); // blob phase reached
		await check(page, info, 'video preview');
		await shot('11-preview-video');
	});
});

test.describe('transcript previews', () => {
	test('allowlisted host auto-loads, others wait for "Load preview"', async ({ page }, info) => {
		await login(page);
		await openByRecent(page, 'Needs your decision');
		const auto = page.getByTestId('media-preview').first();
		await expect(auto).toBeVisible();
		await expect(auto.locator('img')).toHaveAttribute('src', 'https://annielartens.online/results/sample-1.png');
		await expect(auto.locator('img')).toHaveAttribute('referrerpolicy', 'no-referrer');
		await expect.poll(() => auto.locator('img').evaluate((el: HTMLImageElement) => el.naturalWidth)).toBe(320);
		const load = page.getByTestId('load-preview');
		await expect(load).toHaveCount(1);
		await expect(load).toContainText('cdn.example.org');
		await expect(page.locator('video')).toHaveCount(0); // nothing is fetched before the click
		await load.click();
		await expect(page.getByRole('link', { name: /Preview unavailable/ })).toBeVisible(); // 404 from the stub -> graceful fallback
		await check(page, info, 'media previews');
		await auto.scrollIntoViewIfNeeded();
		await shotter(page, info)('09b-inline-media-preview');
	});
});

test.describe('attachments', () => {
	test('upload with progress inserts "Attached file: <path>" at the caret', async ({ page }, info) => {
		const shot = shotter(page, info);
		await login(page);
		await openByRecent(page, 'Needs your decision');
		const box = page.getByRole('textbox', { name: 'Message' });
		await box.fill('look at this please');
		await box.evaluate((el: HTMLTextAreaElement) => { el.setSelectionRange(12, 12); });

		await page.getByTestId('attach-input').setInputFiles({ name: 'photo 1.png', mimeType: 'image/png', buffer: PNG });
		await expect(page.getByTestId('upload-progress')).toBeVisible();
		await expect(page.getByRole('button', { name: 'Attach file' })).toBeDisabled();
		await expect(page.getByRole('progressbar', { name: /Uploading photo 1\.png/ })).toBeVisible();
		await check(page, info, 'uploading');
		await shot('12-attach-uploading');

		await expect(page.getByTestId('upload-progress')).toHaveCount(0);
		await expect(page.getByRole('button', { name: 'Attach file' })).toBeEnabled();
		const value = await box.inputValue();
		expect(value).toMatch(/^look at this\nAttached file: C:\\Users\\mock\\AppData\\Roaming\\TessynDesktop\\attachments\\[0-9a-f]{8}\\20261115-101500-photo 1\.png\n please$/);
		await check(page, info, 'attached');
		await shot('13-attach-inserted');
	});

	test('server errors are shown; the 200 MB cap is checked before uploading', async ({ page }, info) => {
		test.skip(info.project.name.startsWith('webkit'), 'allocating 201 MB in the page is only done on chromium');
		const shot = shotter(page, info);
		await login(page);
		await openByRecent(page, 'Needs your decision');
		await page.getByTestId('attach-input').setInputFiles({ name: 'fail.bin', mimeType: 'application/octet-stream', buffer: Buffer.from('x') });
		await expect(page.getByTestId('attach-error')).toContainText('fail.bin: The daemon could not store the file');
		await expect(page.getByRole('textbox', { name: 'Message' })).toHaveValue('');
		await check(page, info, 'upload error');
		await shot('14-attach-error');
		await page.getByRole('button', { name: 'Dismiss error' }).click();
		await expect(page.getByTestId('attach-error')).toHaveCount(0);

		await page.getByTestId('attach-input').evaluate((el: HTMLInputElement) => {
			const dt = new DataTransfer();
			dt.items.add(new File([new ArrayBuffer(201 * 1024 * 1024)], 'big.mov', { type: 'video/quicktime' }));
			el.files = dt.files;
			el.dispatchEvent(new Event('change', { bubbles: true }));
		});
		await expect(page.getByTestId('attach-error')).toContainText('big.mov');
		await expect(page.getByTestId('attach-error')).toContainText('limit');
		await expect(page.getByTestId('upload-progress')).toHaveCount(0);
	});

	test('the new controls have accessible names', async ({ page }) => {
		await login(page);
		await openByRecent(page, 'Needs your decision');
		await expect(page.getByRole('button', { name: 'Attach file' })).toBeVisible();
		await focusTab(page);
		const critical = page.getByTestId('focus-card').filter({ hasText: 'Production database migration is blocked' });
		await expect(critical.getByRole('list', { name: 'References' })).toBeVisible();
		await expect(critical.getByRole('button', { name: /^Jump to output:/ })).toBeVisible();
		await expect(critical.getByRole('link', { name: /^Open link in a new tab:/ })).toBeVisible();
		await critical.getByRole('button', { name: 'Preview file: Migration plan' }).click();
		const dlg = page.getByRole('dialog', { name: 'Migration plan' });
		await expect(dlg).toHaveAttribute('aria-modal', 'true');
		await expect(dlg.getByRole('button', { name: 'Close preview' })).toBeFocused();
	});
});


// ---- review fixes (311) ---------------------------------------------------------------------------------------------

type MockHook = { liveId(): string; setOnline(v: boolean): void };
const mockOf = <T,>(page: Page, f: (m: MockHook) => T) => page.evaluate((src) => (new Function('m', `return (${src})(m)`))((window as unknown as { __mock: MockHook }).__mock), f.toString());

test.describe('review fixes', () => {
	test('W1: nothing is requested from a non-allowlisted host before the click, markdown images and raw html included', async ({ page }, info) => {
		const hosts: string[] = [];
		page.on('request', (r) => { try { const u = new URL(r.url()); if (u.protocol === 'https:') hosts.push(u.hostname); } catch { /* ignore */ } });
		await login(page);
		await openByRecent(page, 'Needs your decision');
		await expect(page.getByText('Remote-load attempts')).toBeVisible();
		await expect(page.getByTestId('media-preview').first()).toBeVisible(); // the allowlisted one auto-loaded
		await page.waitForTimeout(800);

		const entry = page.locator('.msg.assistant', { hasText: 'Remote-load attempts' });
		await expect(entry.locator('img, video, audio, svg, picture, source, object, embed')).toHaveCount(0);
		await expect(entry.locator('[srcset], [poster], [background]')).toHaveCount(0);
		await expect(entry.getByRole('link', { name: 'secret' })).toHaveAttribute('href', 'https://tracker.evil.example/md?d=SECRET'); // a link, never fetched
		expect(hosts).not.toContain('tracker.evil.example');
		expect(hosts).not.toContain('cdn.example.org'); // not allowlisted: click-to-load
		expect(hosts).toContain('annielartens.online'); // allowlisted: auto-loaded
		await check(page, info, 'hostile entry');

		await page.getByTestId('load-preview').click();
		// WebKit fetches <video> through its media stack, which Playwright does not report as a page request: only check it elsewhere.
		if (info.project.name.startsWith('chromium')) await expect.poll(() => hosts.includes('cdn.example.org')).toBe(true); // only after the click
		else await expect(page.locator('.extras video, .media-failed').first()).toBeVisible();
		expect(hosts).not.toContain('tracker.evil.example');
	});

	test('W2: blob URLs are revoked when the sheet closes', async ({ page }) => {
		await page.addInitScript(() => {
			const w = window as unknown as { __blobs: { created: string[]; revoked: string[] } };
			w.__blobs = { created: [], revoked: [] };
			const c = URL.createObjectURL.bind(URL); const r = URL.revokeObjectURL.bind(URL);
			URL.createObjectURL = (o: Blob | MediaSource) => { const u = c(o); w.__blobs.created.push(u); return u; };
			URL.revokeObjectURL = (u: string) => { w.__blobs.revoked.push(u); r(u); };
		});
		await login(page);
		await openByRecent(page, 'Needs your decision');
		await focusTab(page);
		const critical = page.getByTestId('focus-card').filter({ hasText: 'Production database migration is blocked' });
		await critical.getByRole('button', { name: 'Preview file: photo.png' }).click();
		await expect(page.locator('img.pv-img')).toBeVisible();
		const blobs = () => page.evaluate(() => (window as unknown as { __blobs: { created: string[]; revoked: string[] } }).__blobs);
		const before = await blobs();
		expect(before.created.length).toBeGreaterThan(0);
		expect(before.revoked).toEqual([]);
		await page.getByRole('button', { name: 'Close preview' }).click();
		await expect.poll(async () => (await blobs()).revoked).toEqual(before.created);

		// the pdf blob type is application/pdf and revoked too
		await critical.getByRole('button', { name: 'Preview file: Report (PDF)' }).click();
		await page.getByRole('button', { name: 'Load PDF' }).click();
		const link = page.getByRole('link', { name: 'Open in new tab' });
		await expect(link).toBeVisible();
		const type = await page.evaluate(async (href) => (await (await fetch(href)).blob()).type, (await link.getAttribute('href'))!);
		expect(type).toBe('application/pdf');
		await page.getByRole('button', { name: 'Close preview' }).click();
		await expect.poll(async () => (await blobs()).revoked.length).toBe((await blobs()).created.length);
	});

	test('W4: a foreign hash change drops the Back stack and a stale jump target', async ({ page }) => {
		await login(page);
		await openByRecent(page, 'Importer: schema drift');
		await focusTab(page);
		await page.getByTestId('focus-card').first().getByTestId('ref-entry').click();
		await expect(page.getByTestId('back-bar')).toBeVisible();
		const liveId = await mockOf(page, (m) => m.liveId());
		await page.evaluate((id) => { window.location.hash = `#/s/${id}`; }, liveId);
		await expect(page.locator('.topbar .title-btn')).toContainText('Live: nightly');
		await expect(page.getByTestId('back-bar')).toHaveCount(0);
		// opening the jumped-to session again from the list must show the latest page, not the stale entry
		await openByRecent(page, 'Needs your decision');
		await expect(page.locator('.hl-entry')).toHaveCount(0);
		await expect(page.getByTestId('anchored-bar')).toHaveCount(0);
		await expect(page.getByText('Remote-load attempts')).toBeVisible();
	});

	test('W5: the reading position survives a reconnect (resync)', async ({ page }) => {
		await login(page);
		await openByRecent(page, 'Needs your decision');
		await focusTab(page);
		await page.getByTestId('focus-card').filter({ hasText: 'Which retry policy' }).getByTestId('ref-entry').click();
		await expect(page.getByTestId('anchored-bar')).toBeVisible();
		await expect.poll(() => highlightedText(page)).toBe('typed JSON-RPC client plus an SSE event stream');
		await mockOf(page, (m) => m.setOnline(false));
		await expect(page.getByText('Desktop offline')).toBeVisible();
		await mockOf(page, (m) => m.setOnline(true));
		await expect(page.getByText('Desktop offline')).toHaveCount(0);
		await page.waitForTimeout(600);
		await expect(page.getByTestId('anchored-bar')).toBeVisible();
		expect(await highlightedText(page)).toBe('typed JSON-RPC client plus an SSE event stream');
	});

	test('upload: switching session mid-upload aborts it and inserts nothing anywhere', async ({ page }) => {
		await login(page);
		await openByRecent(page, 'Needs your decision');
		await page.getByTestId('attach-input').setInputFiles({ name: 'slow.png', mimeType: 'image/png', buffer: PNG });
		await expect(page.getByTestId('upload-progress')).toBeVisible();
		const liveId = await mockOf(page, (m) => m.liveId());
		await page.evaluate((id) => { window.location.hash = `#/s/${id}`; }, liveId);
		await expect(page.locator('.topbar .title-btn')).toContainText('Live: nightly');
		await page.waitForTimeout(900);
		await expect(page.getByTestId('upload-progress')).toHaveCount(0);
		await expect(page.getByTestId('attach-error')).toHaveCount(0);
		await expect(page.getByRole('textbox', { name: 'Message' })).toHaveValue('');
		await openByRecent(page, 'Needs your decision');
		await expect(page.getByRole('textbox', { name: 'Message' })).toHaveValue('');
	});

	test('upload: cancel stops the upload without an error', async ({ page }) => {
		await login(page);
		await openByRecent(page, 'Needs your decision');
		await page.getByTestId('attach-input').setInputFiles({ name: 'c.png', mimeType: 'image/png', buffer: PNG });
		await page.getByTestId('upload-progress').getByRole('button', { name: 'Cancel' }).click();
		await expect(page.getByTestId('upload-progress')).toHaveCount(0);
		await expect(page.getByTestId('attach-error')).toHaveCount(0);
		await expect(page.getByRole('button', { name: 'Attach file' })).toBeEnabled();
		await expect(page.getByRole('textbox', { name: 'Message' })).toHaveValue('');
	});
});
