// File preview page. The host sends one of: text (shown with textContent, never parsed), markdown (HTML the host produced with raw
// HTML disabled), image or video (a URL on the preview cache host; anything else is refused here too). Links are never followed by
// the page: a click is posted to the host, which opens http(s) links in the system browser. No host objects exist.
'use strict';
const root = document.getElementById('root');
const CACHE_PREFIX = 'https://preview.local/';

function post(o) { chrome.webview.postMessage(o); }
function clear() { root.replaceChildren(); }
function rendered(kind, ok) { post({ ev: 'rendered', kind: kind, ok: ok }); }

function showText(text) {
  clear(); const pre = document.createElement('pre'); pre.className = 'src'; pre.textContent = String(text); root.appendChild(pre); rendered('text', true);
}
function showMarkdown(html) {
  clear(); const div = document.createElement('div'); div.className = 'md'; div.innerHTML = String(html); root.appendChild(div); rendered('markdown', true);
}
function showMedia(kind, url) {
  clear();
  if (typeof url !== 'string' || !url.startsWith(CACHE_PREFIX)) { rendered(kind, false); return; }
  const el = document.createElement(kind === 'video' ? 'video' : 'img');
  if (kind === 'video') { el.className = 'pvid'; el.controls = true; el.preload = 'metadata'; el.addEventListener('loadedmetadata', () => rendered('video', true)); }
  else { el.className = 'pimg'; el.alt = ''; el.addEventListener('load', () => rendered('image', true)); }
  el.addEventListener('error', () => rendered(kind, false));
  el.src = url; root.appendChild(el);
}

root.addEventListener('click', e => {
  const a = e.target.closest && e.target.closest('a'); if (!a) return;
  e.preventDefault();
  if (a.classList.contains('blocked')) return;
  const href = a.getAttribute('href') || '';
  if (/^https?:\/\//i.test(href)) post({ ev: 'link', href: href });
});

chrome.webview.addEventListener('message', e => {
  const m = e.data; if (!m || typeof m.cmd !== 'string') return;
  switch (m.cmd) {
    case 'text': showText(m.text); break;
    case 'markdown': showMarkdown(m.html); break;
    case 'image': showMedia('image', m.url); break;
    case 'video': showMedia('video', m.url); break;
    case 'clear': clear(); break;
    case 'appearance': if (m.theme === 'dark' || m.theme === 'light') document.documentElement.setAttribute('data-theme', m.theme); break;
  }
});
// Pointer, wheel and key activity goes to the host (W10a screensaver): WPF never sees input that lands in a web page. Throttled; a pointer move counts after it travelled 6 px.
(function () {
  let last = 0, ax = null, ay = null;
  const act = () => { const n = Date.now(); if (n - last < 500) return; last = n; post({ ev: 'activity' }); };
  window.addEventListener('pointermove', e => {
    if (ax !== null && Math.hypot(e.screenX - ax, e.screenY - ay) < 6) return;
    const first = ax === null; ax = e.screenX; ay = e.screenY; if (!first) act();
  }, { passive: true, capture: true });
  for (const t of ['pointerdown', 'wheel', 'keydown']) window.addEventListener(t, act, { passive: true, capture: true });
})();
post({ ev: 'ready' });
