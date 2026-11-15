// Remote media gating for the transcript page (FR.26.4, decision A13). Pure functions, no DOM access, so they run under node in the
// test suite (MediaGateScriptTests). The rule they enforce, and which the host repeats in WebView2TranscriptSurface:
//   - only https URLs without credentials ever become a preview;
//   - a URL on an allowlisted host (exact, or "*.example.com" for subdomains) auto-loads;
//   - every other URL is rendered as a "Load preview <host>" button that carries NO URL: the HTML of a not-yet-loaded preview
//     contains no remote address at all, so nothing can be requested before the click;
//   - at most MAX_PER_ENTRY previews per entry.
'use strict';
(function (root) {
  const MAX_PER_ENTRY = 6;
  const VIDEO_EXT = ['.mp4', '.webm', '.mov', '.m4v', '.ogv'];

  function esc(s) { return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#39;'); }

  function parseHttps(url) {
    let u;
    try { u = new URL(String(url)); } catch (e) { return null; }
    if (u.protocol !== 'https:' || !u.hostname || u.username || u.password) return null;
    return u;
  }

  function hostAllowed(host, hosts) {
    const h = String(host || '').toLowerCase().replace(/\.$/, '');
    if (!h) return false;
    for (const raw of hosts || []) {
      const p = String(raw || '').toLowerCase();
      if (p.startsWith('*.')) { const suffix = p.slice(1); if (h.length > suffix.length && h.endsWith(suffix)) return true; }
      else if (p && h === p) return true;
    }
    return false;
  }

  function kindOf(u) {
    const path = u.pathname.toLowerCase();
    return VIDEO_EXT.some(e => path.endsWith(e)) ? 'video' : 'image';
  }

  // urls: candidate strings from the host; hosts: the allowlist. Returns the previews to offer, each { url, host, kind, auto }.
  function plan(urls, hosts) {
    const out = [], seen = new Set();
    for (const raw of urls || []) {
      if (out.length >= MAX_PER_ENTRY) break;
      const u = parseHttps(raw);
      if (!u || seen.has(u.href)) continue;
      seen.add(u.href);
      out.push({ url: u.href, host: u.hostname.toLowerCase(), kind: kindOf(u), auto: hostAllowed(u.hostname, hosts) });
    }
    return out;
  }

  function elementHtml(item) {
    if (item.kind === 'video') return '<video class="mvid" src="' + esc(item.url) + '" controls preload="metadata" referrerpolicy="no-referrer"></video>';
    return '<img class="mimg" src="' + esc(item.url) + '" alt="" loading="lazy" decoding="async" referrerpolicy="no-referrer">';
  }

  // The HTML under an entry: one slot per previewable link. loaded: Set of "entry:slot" keys the user already clicked.
  function previewHtml(urls, hosts, entryIndex, loaded) {
    const items = plan(urls, hosts);
    if (!items.length) return '';
    let html = '<div class="mp">';
    items.forEach((item, j) => {
      const isLoaded = item.auto || (loaded && loaded.has(entryIndex + ':' + j));
      html += '<div class="ms">' + (isLoaded ? elementHtml(item)
        : '<button class="mload" type="button" data-i="' + entryIndex + '" data-j="' + j + '">Load preview ' + esc(item.host) + '</button>') + '</div>';
    });
    return html + '</div>';
  }

  const api = { MAX_PER_ENTRY, plan, hostAllowed, previewHtml, elementHtml, esc };
  root.MediaGate = api;
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
})(typeof globalThis !== 'undefined' ? globalThis : this);
