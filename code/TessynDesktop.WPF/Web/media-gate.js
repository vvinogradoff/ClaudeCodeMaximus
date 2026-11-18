// Remote media for the transcript page (FR.26.4, decision A13, review V2). Pure functions, no DOM access, so they run under node in the
// test suite (MediaGateScriptTests). The page never loads a remote URL itself:
//   - only https URLs without credentials ever become a preview, and the URL string is used exactly as the host sent it (no re-normalising),
//     because the host approves and serves exactly the strings it offered;
//   - an IMAGE on an allowlisted host (exact, or "*.example.com" for subdomains) auto-loads, through the host's media proxy
//     (https://media.local/i?u=...), which fetches it, re-checks redirects and caps type and size;
//   - every other preview, and every video, is a "Load preview <host>" button that carries NO URL: the markup of a not-yet-loaded preview
//     contains no remote address at all. After the click the host approves it; an image then loads through the proxy, a video is downloaded
//     by the host and played from the local preview cache (https://preview.local/...);
//   - at most MAX_PER_ENTRY previews per entry; "loaded" is keyed by URL (Map url -> src), not by position.
'use strict';
(function (root) {
  const MAX_PER_ENTRY = 6;
  const VIDEO_EXT = ['.mp4', '.webm', '.mov', '.m4v', '.ogv'];
  const PROXY = 'https://media.local/i?u=';
  const CACHE = 'https://preview.local/';

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
      if (!u || seen.has(raw)) continue;
      seen.add(raw);
      const kind = kindOf(u);
      out.push({ url: String(raw), host: u.hostname.toLowerCase(), kind: kind, auto: kind === 'image' && hostAllowed(u.hostname, hosts) });
    }
    return out;
  }

  // The element of a loaded preview. src is the cache URL the host returned for a video; an image always goes through the proxy.
  function elementHtml(item, src) {
    if (item.kind === 'video') {
      if (typeof src !== 'string' || !src.startsWith(CACHE)) return '';
      return '<video class="mvid" src="' + esc(src) + '" controls preload="metadata"></video>';
    }
    return '<img class="mimg" src="' + esc(PROXY + encodeURIComponent(item.url)) + '" alt="" loading="lazy" decoding="async" referrerpolicy="no-referrer">';
  }

  // The HTML under an entry: one slot per previewable link. loaded: Map url -> src (empty for an image) the user already clicked; null = failed.
  function previewHtml(urls, hosts, entryIndex, loaded) {
    const items = plan(urls, hosts);
    if (!items.length) return '';
    let html = '<div class="mp">';
    items.forEach((item, j) => {
      const has = loaded && loaded.has(item.url);
      let inner;
      if (has && loaded.get(item.url) === null) inner = '<span class="mfail">Preview unavailable</span>';
      else if (item.auto || has) inner = elementHtml(item, has ? loaded.get(item.url) : '') || '<span class="mfail">Preview unavailable</span>';
      else inner = '<button class="mload" type="button" data-i="' + entryIndex + '" data-j="' + j + '">Load preview ' + esc(item.host) + '</button>';
      html += '<div class="ms" data-j="' + j + '">' + inner + '</div>';
    });
    return html + '</div>';
  }

  const api = { MAX_PER_ENTRY, PROXY, CACHE, plan, hostAllowed, previewHtml, elementHtml, esc };
  root.MediaGate = api;
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
})(typeof globalThis !== 'undefined' ? globalThis : this);
