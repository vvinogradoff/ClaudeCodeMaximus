// Minimal app-shell service worker: caches hashed assets (cache-first) and index.html (network-first).
// Never touches /api/* or /link/*.
const CACHE = 'tessyn-shell-v1';
const SHELL = ['/', '/index.html'];

self.addEventListener('install', (event) => {
	event.waitUntil(caches.open(CACHE).then((c) => c.addAll(SHELL)).then(() => self.skipWaiting()));
});

self.addEventListener('activate', (event) => {
	event.waitUntil(
		caches.keys().then((keys) => Promise.all(keys.filter((k) => k !== CACHE).map((k) => caches.delete(k)))).then(() => self.clients.claim()),
	);
});

self.addEventListener('fetch', (event) => {
	const req = event.request;
	if (req.method !== 'GET') return;
	const url = new URL(req.url);
	if (url.origin !== self.location.origin) return;
	if (url.pathname.startsWith('/api/') || url.pathname.startsWith('/link/') || url.pathname === '/healthz') return;

	if (url.pathname.startsWith('/assets/')) {
		event.respondWith(
			caches.match(req).then((hit) => hit || fetch(req).then((res) => {
				if (res.ok) { const copy = res.clone(); caches.open(CACHE).then((c) => c.put(req, copy)); }
				return res;
			})),
		);
		return;
	}

	// index.html / navigations / manifest / icons: network first, fall back to cache when offline.
	event.respondWith(
		fetch(req).then((res) => {
			if (res.ok && (req.mode === 'navigate' || SHELL.includes(url.pathname))) {
				const copy = res.clone();
				caches.open(CACHE).then((c) => c.put(req.mode === 'navigate' ? '/index.html' : req, copy));
			}
			return res;
		}).catch(() => caches.match(req.mode === 'navigate' ? '/index.html' : req)),
	);
});
