# Tessyn Web — operations guide

Requirements: FR.25 in [requirements.md](/docs/requirements.md). Use cases: [UC.WEB.01](/docs/usecases/UC.WEB.01-Web-Client.md). Test cases: [TC.WEB.01](/docs/testcases/TC.WEB.01-Web-Client.md). Remote API: [tdcp-spec.md §8](/docs/refactoring/tdcp-spec.md).

> Synced with the built system on 2026-11-13 (deployed to `safe`, hardened with the dedicated `tessyn-web` user). Paths and names below are the real ones, taken from `code/Tessyn.Web/deploy/` and the work reports. The live positive login from the phone has not been done yet (todo Phase 40).

## 1. What runs where

```
iPhone (PWA)                   server "safe" (84.247.128.142)                       Windows PC
─────────────                  ─────────────────────────────                        ──────────
https://tessyn.vvinogradoff.com ──► nginx :443 (TLS, certbot)
   POST /api/rpc                      └─► Tessyn.Web 127.0.0.1:5320 (systemd tessyn-web, user tessyn-web)
   GET  /api/events (SSE)                   • cookie login, throttling, allowlist
                                            • serves the single-page app (wwwroot)
                                            ▲  GET /link/stream, POST /link/reply, POST /link/events
                                            └──────────────── dialled OUT by TessynDesktop ◄─ Relay Connector
```

- The PC opens **no inbound port**. TessynDesktop connects out to the server.
- The server keeps no Tessyn data. If the PC is off, the site loads and says **Desktop offline**.
- Secrets: the web password (as a hash) and the link token (as a hash) are in the server config; the link token itself is in `%AppData%\TessynDesktop\web-relay.dat` on the PC, encrypted with DPAPI.
- The service runs as its own system user **`tessyn-web`** (no home, `nologin`), **not** `www-data`: `safe` hosts other sites as `www-data`, and anything running as `www-data` could otherwise read the Data Protection keys and forge login cookies.

| What | Where |
|---|---|
| App files | `/var/www/tessyn` (owner `masteruser`, group `tessyn-web`, 750, files 640 + `x` on `Tessyn.Web`; read-only to the service) |
| Server config (secrets) | `/etc/tessyn-web/appsettings.Production.json` (`root:tessyn-web` 640; the directory `root:tessyn-web` 750) |
| State | `/var/lib/tessyn-web` (`tessyn-web`, 700): `keys/` (Data Protection keys, 700) and `auth-epoch` (600, created on the first login request) |
| systemd unit | `/etc/systemd/system/tessyn-web.service` (installed by `redeploy.sh` from `deploy/tessyn-web.service`) |
| nginx site | `/etc/nginx/sites-available/tessyn.vvinogradoff.com` (linked in `sites-enabled`; the copy in `deploy/nginx-tessyn.vvinogradoff.com.conf` equals the live file) |
| Certificate | Let's Encrypt (certbot), expires 2027-02-10, renewed by the existing certbot timer |
| Build and deploy assets | `code/Tessyn.Web/deploy/` (`publish.ps1`, `redeploy.sh`, `tessyn-web.service`, `nginx-tessyn.vvinogradoff.com.conf`, `appsettings.Production.example.json`) |
| Desktop relay config | `%AppData%\TessynDesktop\web-relay.dat` |
| Fleet inventory | the vhost, the unit and port 5320 are listed in the fleet's `server-matrix.md` (QuickBooksSQL `Docs/operations`) |

`safe` is shared with other services (the fleet's Grafana, Portfolio, GregFisher, MentalCure). Never change nginx without `sudo nginx -t` first, and never reload it if the test fails.

The unit sandbox (`systemd-analyze security` 1.8 OK): `NoNewPrivileges`, `PrivateTmp`, `PrivateDevices`, `ProtectSystem=strict` (only `/var/lib/tessyn-web` is writable), `ProtectHome`, `ProtectKernel*`, `ProtectControlGroups`, `RestrictAddressFamilies=AF_INET AF_INET6 AF_UNIX`, empty capability sets, `LockPersonality`, `RestrictNamespaces`, `RestrictRealtime`, `RestrictSUIDSGID`, `SystemCallFilter=@system-service`, `UMask=0077`. `MemoryDenyWriteExecute` is deliberately off (it breaks the .NET JIT).

## 2. First-time setup (already done on `safe`; kept for a rebuild of the box)

1. **DNS.** An A record `tessyn` → `84.247.128.142` for `vvinogradoff.com` (TTL 300). Check: `dig +short tessyn.vvinogradoff.com @1.1.1.1`.
2. **Folders** (on `safe`): `/var/www/tessyn` (`masteruser:tessyn-web`, 750), `/etc/tessyn-web` (`root:tessyn-web`, 750), `/var/lib/tessyn-web/keys` (`tessyn-web:tessyn-web`, 700). `redeploy.sh` creates the user and fixes these permissions (idempotent); only the config file itself must be created by hand.
3. **nginx and certificate.** Install the vhost from `deploy/nginx-tessyn.vvinogradoff.com.conf`, run `sudo nginx -t`, reload, then `sudo certbot --nginx -d tessyn.vvinogradoff.com`. Check that the 443 block has `Strict-Transport-Security`, that `/api/events` and `/link/stream` have `proxy_buffering off` and `proxy_read_timeout 3600s`, that both locations set `X-Forwarded-For $remote_addr` (overwrite, not `$proxy_add_x_forwarded_for`) and that `client_max_body_size` is `4M`.
4. **Secrets.** Make the password hash and the link token (sections 4 and 5), then create `/etc/tessyn-web/appsettings.Production.json` from `deploy/appsettings.Production.example.json`:
   ```json
   {
     "TessynWeb": {
       "Auth": { "Email": "<login email>", "PasswordHash": "pbkdf2-sha256$600000$…$…", "CookieDays": 14, "AbsoluteDays": 30 },
       "Link": { "TokenSha256": "<64 hex digits>" },
       "DataProtectionKeysPath": "/var/lib/tessyn-web/keys"
     }
   }
   ```
   `sudo chown root:tessyn-web` and `sudo chmod 640`. **A value in this file overrides the code default**, so keep `CookieDays` 14 (idle) and `AbsoluteDays` 30 (absolute) unless you mean to change them.
5. **First deploy** (section 3); `redeploy.sh` enables and starts the unit.
6. **Desktop.** In TessynDesktop → Settings → **Web access**: tick Enabled, URL `https://tessyn.vvinogradoff.com`, paste the link token, **Save**. The status line must reach **Connected since …**.
7. Open the site on the phone, log in, then Share → Add to Home Screen.

## 3. Deploy and redeploy

On the Windows PC, in the repository root (the web projects live in the `web-client` worktree until merged; run from the tree that holds `code/Tessyn.Web`):

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\code\Tessyn.Web\deploy\publish.ps1            # add -SkipClient when wwwroot is already fresh
bash code/Tessyn.Web/deploy/redeploy.sh         # Git Bash; optional argument: path to the tarball
```

- `publish.ps1` runs `npm ci` and `npm run build` in `ClientApp`, then `dotnet publish -c Release -r linux-x64 --self-contained true` (the server has no .NET 10 runtime; the app ships its own) and packs `code/Tessyn.Web/artifacts/tessyn-web.tar.gz` (about 48 MB). It refuses to pack when `wwwroot/index.html` is missing.
- `redeploy.sh` (host from `TESSYN_DEPLOY_HOST`, default `safe`; needs key-based `ssh`/`scp`): uploads the tarball and the unit, creates the `tessyn-web` user if missing, installs the unit and reloads systemd, stops the service, fixes the state and config permissions, empties `/var/www/tessyn` and extracts the new build, restores the permissions (750, files 640, `x` kept), removes the tarball, starts the service, then checks `/healthz` on the box (loopback) and publicly. It ends with `healthz (loopback): {"ok":true,"linkOnline":…}`.
- The public `/healthz` answers only `{"ok":true}`; **`linkOnline` is visible only on the box** (`ssh safe curl http://127.0.0.1:5320/healthz`), so use that to see whether the PC is connected.
- The config, the keys and the auth epoch are outside `/var/www/tessyn`, so a redeploy does not touch them and users stay logged in.
- There is no `.old` folder: to roll back, check out the previous commit, publish again and redeploy.
- The desktop reconnects by itself (backoff up to 30 s) while the server restarts.
- A new web version reaches the phone on the next launch (`index.html` is fetched network-first).

## 4. Change the web password

1. On the PC (or anywhere with the published binary): `echo -n 'the new password' | ./Tessyn.Web hash-password` (it reads the password from standard input; `dotnet run -- hash-password` from `code/Tessyn.Web` works too). It prints one line `pbkdf2-sha256$600000$…`. Use a long random password; this password is the only barrier to a Claude Code session on the PC.
2. On `safe`, edit `/etc/tessyn-web/appsettings.Production.json` and replace `TessynWeb:Auth:PasswordHash` with that line.
3. `sudo systemctl restart tessyn-web`.
4. **Every device is logged out.** The password hash is part of each cookie's security stamp, so all auth cookies and all trusted-device cookies issued before the change stop working at once; you do not need to delete the keys any more. Log in again on each device. (Logging out on one device does the same: see section 9.)

## 5. Rotate the link token

1. Run `Tessyn.Web new-link-token`. It prints `token: …` and `sha256: <hex>`. The hash is the SHA-256 of the token text exactly as printed.
2. On `safe`, put the hash in `TessynWeb:Link:TokenSha256` and `sudo systemctl restart tessyn-web`. The old token stops working at once, so the desktop shows **Error: Link token rejected** until step 3.
3. **On the desktop**, either:
   - Settings → **Web access**: paste the new token into the (write-only) token box and press **Save**; the status goes to **Connected since …**. An empty token box keeps the stored one. Or
   - replace the DPAPI file `%AppData%\TessynDesktop\web-relay.dat` (a JSON `{ "enabled", "url", "token" }`, DPAPI-protected for the current user) with a provisioned one and restart TessynDesktop or press Save in Settings. The connector reads the file at start only.
4. Do not store the token anywhere else; the Settings page never shows it again.

## 6. Turn web access on or off on the desktop

- **Off:** Settings → Web access → untick Enabled → Save. The Connector stops at once; the site shows **Desktop offline**. The URL and token stay saved.
- **On:** tick Enabled → Save. No restart of TessynDesktop is needed.
- Closing TessynDesktop or putting the PC to sleep has the same effect as Off. When the PC wakes up the Connector reconnects by itself.
- To remove the setting completely, delete `%AppData%\TessynDesktop\web-relay.dat`.

## 7. Logs

- Server: `ssh safe "sudo journalctl -u tessyn-web -n 200 --no-pager"`; follow with `-f`. At Information level the log has no passwords, tokens, prompts or RPC payloads. Failed logins and link connects are logged with the real client IP. One known warning at start: `No XML encryptor configured` (the Data Protection keys are stored unencrypted; the directory is readable by `tessyn-web` only).
- nginx: `/var/log/nginx/access.log` and `error.log`.
- Desktop: the TessynDesktop log (`%AppData%\TessynDesktop\logs`); the Connector logs connect, disconnect and the reason for a failure, never the token.
- Service state: `systemctl status tessyn-web`. Certificate: `sudo certbot certificates` (renewal is automatic).

## 8. Troubleshooting

| Symptom | Cause and fix |
|---|---|
| Site says **Desktop offline** | The Daemon Link is down. Check that TessynDesktop runs and the PC is awake; Settings → Web access shows the status. On the box `curl http://127.0.0.1:5320/healthz` shows `linkOnline: false` (the public endpoint no longer says). If the status is **Error**, read its reason, then see the next rows. |
| Desktop status **Error: Link token rejected**, server log shows 401 on `/link/stream` | The token on the desktop does not match `Link:TokenSha256` on the server. Rotate the token (section 5) or paste the right one. |
| Desktop status **Error**, cannot connect, or **Invalid web server URL** | Wrong URL, DNS, or the server is down: `curl -I https://tessyn.vvinogradoff.com`, `systemctl status tessyn-web`. A 502 from nginx means the service is not running. |
| Desktop status **Error: Link closed by the server** | The server restarted, or another link replaced this one. The connector reconnects by itself. |
| **Connected** but the phone stays offline | The server has a different link (a second PC or test instance replaced yours). Only one link can be active; the last one connected wins. Stop the other one. |
| Login gives **429** | Throttled: 5 failed logins in 15 minutes from that IP, or 20 in an hour for everyone (a device that has logged in before is exempt from the global limit but not from the per-IP one). Wait for `Retry-After` (shown on the login page). A script or a wrong saved password in a password manager is the usual cause. Restarting the service clears the counters. |
| Login gives **401** with the right password | The hash in the config does not match: redo section 4 and check that the JSON is valid (the server refuses to start otherwise). The email is compared too. |
| Login or logout gives **400** | The `X-Tessyn-Client: web` header is missing: a stale cached app, or a script calling the API (login and logout need it too). Reload the app. |
| Login gives **413** | The body exceeds 4 KB; not a normal login. |
| Logged out on every device unexpectedly | A logout on any device, a password change, a deleted `auth-epoch` file, or the 30-day absolute limit. Sessions idle for 14 days also end. |
| Updates arrive late or in bursts | nginx buffers the stream: check `proxy_buffering off` and `X-Accel-Buffering` on `/api/events` and `/link/stream`. |
| Phone shows old data after switching apps | Expected for a few seconds: the page resyncs when it becomes visible. If it never does, reload; check the server log for a crash. |
| Service will not start or crash-loops | `journalctl -u tessyn-web`: a missing hash, missing token hash or unreadable config file stops it with a clear message. Check `sudo ls -l /etc/tessyn-web` (directory `root:tessyn-web` 750, file `root:tessyn-web` 640) and `/var/lib/tessyn-web` (`tessyn-web`, 700). The deploy user cannot list the 750 config directory, so use `sudo test`/`sudo ls`; `redeploy.sh` does. |
| Request fails with `-32002` (timeout) | The daemon took longer than 30 s (60 s for entries, tree and searches). The PC may be busy or the session file very large; retry. |
| Usage strip missing | `usage.get` answered `pending`, `unauthorized` or `unavailable` (the strip is hidden in those states); it appears when the first fetch finishes (`usage.changed`). |
| After a TessynDesktop restart the transcript reloads | Expected: session epochs restart at 0, so the app reloads the open transcript once. |
| A session hidden from the web Recent still shows in the desktop's Recent dropdown | The desktop refreshes its copy when `recent.hide` raises `SessionSettingsChanged`; an app started before that fix needs one restart (FR.25.14). |

## 9. Security notes

- The site gives remote control of Claude Code on the PC, so the password is the only barrier. Use a long random password; a second factor (TOTP) is a recommended follow-up (FR.25.39, todo Phase 40).
- **Sessions.** The auth cookie `__Host-Tessyn.Auth` slides with a 14-day idle limit and ends 30 days after login at the latest. It carries a security stamp; **logging out on any device, or changing the password, logs out every device**. A device that logged in successfully also holds `__Host-Tessyn.Device` (180 days), which only exempts it from the global login limit.
- **Isolation.** The service user `tessyn-web` owns the keys and can read the config; `www-data` and the deploy user cannot read the keys. The Data Protection keys are not encrypted at rest (follow-up).
- Keep `appsettings.Production.json` out of git and out of backups that others can read.
- The link token is separate from the password; rotate it if the PC's `%AppData%` was exposed. Anyone with the token can pose as the PC (and read what the browsers ask) **and can drive the desktop's remote methods**, but cannot log in as the user.
- No filesystem paths, agent tokens, Claude session ids or profile directories are sent over the relay (FR.25.07). A logout clears saved drafts from the phone's local storage.
- nginx has no `limit_req` yet (follow-up); login throttling is done by the app.
