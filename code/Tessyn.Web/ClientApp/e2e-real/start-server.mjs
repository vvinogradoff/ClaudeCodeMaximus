// Starts the REAL Tessyn.Web (Development) on 127.0.0.1:5399 with a throw-away config (test password + link-token hash
// generated with the CLI) and serves the production SPA build from ../wwwroot. Used by playwright.real.config.ts.
// Prerequisites: `npm run build` (production SPA) and `dotnet build ../Tessyn.Web.csproj`.
import { spawn, spawnSync } from 'node:child_process';
import { mkdtempSync, writeFileSync, rmSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

export const E2E_EMAIL = 'e2e@example.test';
export const E2E_PASSWORD = 'e2e-correct-horse-battery';
const PORT = 5399;

const here = dirname(fileURLToPath(import.meta.url));
const webRoot = resolve(here, '..', '..');
const dll = join(webRoot, 'bin', 'Debug', 'net10.0', 'Tessyn.Web.dll');
if (!existsSync(dll)) { console.error(`Missing ${dll}: run "dotnet build" in code/Tessyn.Web first.`); process.exit(1); }
if (!existsSync(join(webRoot, 'wwwroot', 'index.html'))) { console.error('Missing wwwroot/index.html: run "npm run build" first.'); process.exit(1); }

const cli = (args, input) => {
	const r = spawnSync('dotnet', [dll, ...args], { input, encoding: 'utf8', cwd: webRoot });
	if (r.status !== 0) { console.error(r.stderr); process.exit(1); }
	return r.stdout;
};

const passwordHash = cli(['hash-password'], E2E_PASSWORD + '\n').trim();
const tokenSha = /sha256: ([0-9a-f]{64})/.exec(cli(['new-link-token']))?.[1];
if (!passwordHash || !tokenSha) { console.error('CLI did not produce a hash/token'); process.exit(1); }

const dir = mkdtempSync(join(tmpdir(), 'tessyn-web-e2e-'));
const configPath = join(dir, 'appsettings.e2e.json');
writeFileSync(configPath, JSON.stringify({
	TessynWeb: {
		Auth: { Email: E2E_EMAIL, PasswordHash: passwordHash },
		Link: { TokenSha256: tokenSha },
		DataProtectionKeysPath: join(dir, 'keys'),
	},
}));

const server = spawn('dotnet', [dll], {
	cwd: webRoot,
	stdio: 'inherit',
	env: {
		...process.env,
		ASPNETCORE_ENVIRONMENT: 'Development',
		ASPNETCORE_URLS: `http://127.0.0.1:${PORT}`,
		TESSYN_WEB_CONFIG: configPath,
	},
});

const stop = () => {
	server.kill();
	try { rmSync(dir, { recursive: true, force: true }); } catch { /* best effort */ }
};
process.on('SIGINT', () => { stop(); process.exit(0); });
process.on('SIGTERM', () => { stop(); process.exit(0); });
process.on('exit', stop);
server.on('exit', (code) => process.exit(code ?? 0));
