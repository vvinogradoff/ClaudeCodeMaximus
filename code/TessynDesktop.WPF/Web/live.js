// The live row of a turn in flight (W1a): status with a timer, tool calls, the thinking and the text streamed so far. Everything is set as
// text (textContent), never as HTML, so streamed model output cannot inject markup. Pure helpers are tested under node (LiveScriptTests).
'use strict';
(function (root) {
  const TEXT_MAX = 20000;

  // "7s", "1m 05s": the timer next to the status.
  function elapsedText(ms) {
    const s = Math.max(0, Math.floor(ms / 1000)), m = Math.floor(s / 60);
    return m > 0 ? m + 'm ' + String(s % 60).padStart(2, '0') + 's' : s + 's';
  }

  // The newest part of a long text, cut at a line start where possible.
  function tail(text, max) {
    if (typeof text !== 'string') return '';
    if (text.length <= max) return text;
    const cut = text.length - max, nl = text.indexOf('\n', cut);
    return '...' + text.substring(nl >= 0 && nl - cut < 200 ? nl : cut);
  }

  function el(doc, cls, text) { const e = doc.createElement('div'); e.className = cls; if (text !== undefined && text !== null) e.textContent = text; return e; }

  // Builds the row for `live` (see TranscriptLiveView); returns null when there is nothing to show.
  function build(doc, live, now) {
    if (!live) return null;
    const box = el(doc, 'lv');
    (live.queued || []).forEach(q => box.appendChild(el(doc, 'lq', 'Queued: ' + q)));
    if (live.startedAt != null || live.status) {
      const h = el(doc, 'lh');
      h.appendChild(el(doc, 'ld'));
      h.appendChild(doc.createTextNode((live.status || '') + ' '));
      if (live.startedAt != null) { const t = doc.createElement('span'); t.className = 'lt'; t.setAttribute('data-start', String(live.startedAt)); t.textContent = elapsedText(now - live.startedAt); h.appendChild(t); }
      if (live.usage) h.appendChild(doc.createTextNode(' - ' + live.usage));
      box.appendChild(h);
    }
    (live.tools || []).forEach(t => {
      const row = el(doc, 'ltool' + (t.done ? ' done' : '') + (t.isError ? ' err' : ''));
      row.textContent = t.name + (t.summary ? ': ' + t.summary : '') + (t.done ? (t.isError ? '  [failed]' : '  [done]') : '');
      box.appendChild(row);
    });
    if (live.thinking) box.appendChild(el(doc, 'lk', tail(live.thinking, 2000)));
    if (live.text) box.appendChild(el(doc, 'ltx', tail(live.text, TEXT_MAX)));
    if (live.error) box.appendChild(el(doc, 'lerr', live.error));
    return box.children.length ? box : null;
  }

  const api = { elapsedText, tail, build };
  root.LiveView = api;
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
})(typeof globalThis !== 'undefined' ? globalThis : this);
