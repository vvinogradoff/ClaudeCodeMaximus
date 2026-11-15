// Paging gate for newer pages (a transcript view that started at a jump target stops short of the newest entry). Pure functions, so they run
// under node in the test suite (PagingScriptTests). The rule: one request at a time; after a FAILED request the page does not ask again
// until the user scrolls (the host shows a Retry button for the immediate case), so a failing daemon cannot cause a request loop (N3).
'use strict';
(function (root) {
  function create() { return { pending: false, failedAt: null }; }

  // Should the page post "needNewer" now?
  function shouldRequest(g, hasNewer, scrollTop, clientHeight, scrollHeight, threshold) {
    if (!hasNewer || g.pending) return false;
    if (g.failedAt !== null && Math.abs(scrollTop - g.failedAt) < 1) return false;
    return scrollTop + clientHeight >= scrollHeight - threshold;
  }
  function markRequested(g) { g.pending = true; }
  function markLoaded(g) { g.pending = false; g.failedAt = null; }
  function markFailed(g, scrollTop) { g.pending = false; g.failedAt = scrollTop; }
  // The user moved the view: a failed request may be tried again.
  function onUserScroll(g, scrollTop) { if (g.failedAt !== null && Math.abs(scrollTop - g.failedAt) >= 1) g.failedAt = null; }

  const api = { create, shouldRequest, markRequested, markLoaded, markFailed, onUserScroll };
  root.PagingGate = api;
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
})(typeof globalThis !== 'undefined' ? globalThis : this);
