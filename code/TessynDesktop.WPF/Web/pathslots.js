// Inline local paths of the transcript page (FR.26.5). Pure functions, no DOM access, so they run under node in the test suite (PathSlotsScriptTests).
// The host marks each path inline (span.pth) and puts an empty span.pslot after the first occurrence of an image or video path; this module turns the slot
// into the image preview (directly under the path) from the state the host reported, and decides which clicks open a path or link.
'use strict';
(function (root) {
  const SLOT_RE = /<span class="pslot" data-j="(\d+)"><\/span>/g;

  function esc(s) { return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#39;'); }
  const VIDEO_RE = /\.(mp4|mov|webm|mkv|avi|m4v|wmv)$/i;   // the host's Constants.FileTypes.InlineVideo (FR.26.6)
  function isVideo(path) { return VIDEO_RE.test(String(path)); }
  function leaf(path) { return path.substring(Math.max(path.lastIndexOf('\\'), path.lastIndexOf('/')) + 1); }

  // The slot for path j of entry i. state: null/undefined (not asked yet), { st: 'wait' | 'ok' | 'denied' | 'fail', src }.
  // Only a cache URL of the app's own preview host ever becomes an img src. A video path shows the first frame (an image from the cache) with a
  // play badge over it (span.pplay, no pointer events: a click lands on the frame); its hints are "Video not allowed" and "Preview unavailable".
  function slotHtml(entryIndex, j, path, state) {
    const open = '<span class="pslot" data-j="' + j + '">', video = isVideo(path);
    if (state && state.st === 'ok' && typeof state.src === 'string' && state.src.startsWith('https://preview.local/')) {
      const img = '<img class="pimg" src="' + esc(state.src) + '" alt="' + esc(leaf(path)) + '" data-i="' + entryIndex + '" data-j="' + j + '" referrerpolicy="no-referrer" decoding="async">';
      return open + (video ? '<span class="pvid">' + img + '<span class="pplay" aria-hidden="true"></span></span>' : img) + '</span>';
    }
    if (state && state.st === 'denied') return open + '<span class="pnote">' + (video ? 'Video not allowed' : 'Image not allowed') + '</span></span>';
    if (state && state.st === 'fail') return open + '<span class="pnote">' + (video ? 'Preview unavailable' : 'Image unavailable') + '</span></span>';
    return open + '<span class="pnote">' + (video ? 'Loading preview' : 'Loading image') + '&hellip;</span></span>';
  }

  // The entry's html with its slots filled. stateOf(path) returns the state (or null); the paths nobody asked for yet come back in "wanted".
  function fill(html, entryIndex, paths, stateOf) {
    const wanted = [];
    if (!paths || !paths.length || html.indexOf('pslot') < 0) return { html: html, wanted: wanted };
    const out = html.replace(SLOT_RE, (all, j) => {
      const idx = parseInt(j, 10), path = paths[idx];
      if (typeof path !== 'string') return '';
      const st = stateOf(path);
      if (!st) wanted.push(path);
      return slotHtml(entryIndex, idx, path, st);
    });
    return { html: out, wanted: wanted };
  }

  // Opening needs the plain left button with Ctrl (and no Shift/Alt); a plain click only selects, so text and images can be copied.
  function opens(ev) { return !!ev && ev.type === 'click' && ev.button === 0 && !!ev.ctrlKey && !ev.shiftKey && !ev.altKey; }

  // FR.26.9: Ctrl+Shift+left-click on a link sends it to the system browser (a path never uses it: opens() needs no Shift).
  function forcesBrowser(ev) { return !!ev && ev.type === 'click' && ev.button === 0 && !!ev.ctrlKey && !!ev.shiftKey && !ev.altKey; }

  const api = { fill, slotHtml, opens, forcesBrowser, esc, leaf, isVideo };
  root.PathSlots = api;
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
})(typeof globalThis !== 'undefined' ? globalThis : this);
