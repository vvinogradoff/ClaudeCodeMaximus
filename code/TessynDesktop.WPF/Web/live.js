// The live row of a turn in flight (W1a): status with a timer, tool calls, the thinking and the text streamed so far. Everything is set as
// text (textContent / text node data), never as HTML, so streamed model output cannot inject markup. The row is created once and updated in
// place: the streamed text grows by appending to one text node, so a selection in it survives the updates. Pure helpers and the row itself are
// tested under node with a fake document (LiveScriptTests).
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

  function el(doc, cls) { const e = doc.createElement('div'); e.className = cls; return e; }

  // Sets the text of `holder` (a div holding one text node). A text that only grew is appended, so a selection inside it is not destroyed.
  function setText(doc, holder, value) {
    let node = holder.firstChild;
    if (!node) { node = doc.createTextNode(''); holder.appendChild(node); }
    const old = node.data;
    if (old === value) return;
    if (value.length > old.length && value.startsWith(old)) node.appendData(value.substring(old.length)); else node.data = value;
  }

  // Makes the row: { el, update(live, now) -> bool (anything to show), startAt (ms or null), tickText(now) }.
  function create(doc) {
    const box = el(doc, 'lv'), queued = el(doc, 'lqs'), head = el(doc, 'lh'), dot = el(doc, 'ld'), status = el(doc, 'ls'), timer = doc.createElement('span');
    const tools = el(doc, 'ltools'), thinking = el(doc, 'lk'), text = el(doc, 'ltx'), error = el(doc, 'lerr');
    timer.className = 'lt';
    head.appendChild(dot); head.appendChild(status); head.appendChild(timer);
    [queued, head, tools, thinking, text, error].forEach(c => box.appendChild(c));
    const view = { el: box, startAt: null };

    function setVisible(node, on) { node.style.display = on ? '' : 'none'; }

    view.tickText = function (now) { return view.startAt == null ? '' : elapsedText(now - view.startAt); };

    view.update = function (live, now) {
      if (!live) { view.startAt = null; box.style.display = 'none'; return false; }
      view.startAt = live.startedAt == null ? null : live.startedAt;

      // queued rows: few and short, rebuilt only when they change
      const q = (live.queued || []).join('\n');
      if (queued.dataset.q !== q) { queued.dataset.q = q; queued.replaceChildren(); (live.queued || []).forEach(t => { const r = el(doc, 'lq'); r.textContent = 'Queued: ' + t; queued.appendChild(r); }); }

      const showHead = view.startAt != null || !!live.status;
      setVisible(head, showHead);
      setText(doc, status, (live.status || '') + (live.usage ? ' - ' + live.usage : '') + ' ');
      setVisible(timer, view.startAt != null);
      timer.textContent = view.tickText(now);

      // tools: updated by position
      const list = live.tools || [];
      while (tools.children.length > list.length) tools.removeChild(tools.lastChild);
      list.forEach((t, i) => {
        let row = tools.children[i];
        if (!row) { row = el(doc, 'ltool'); tools.appendChild(row); }
        const cls = 'ltool' + (t.done ? ' done' : '') + (t.isError ? ' err' : '');
        if (row.className !== cls) row.className = cls;
        const label = t.name + (t.summary ? ': ' + t.summary : '') + (t.done ? (t.isError ? '  [failed]' : '  [done]') : '');
        if (row.textContent !== label) row.textContent = label;
      });

      setText(doc, thinking, tail(live.thinking || '', 2000)); setVisible(thinking, !!live.thinking);
      setText(doc, text, tail(live.text || '', TEXT_MAX)); setVisible(text, !!live.text);
      setText(doc, error, live.error || ''); setVisible(error, !!live.error);

      const any = showHead || (live.queued || []).length > 0 || list.length > 0 || !!live.thinking || !!live.text || !!live.error;
      setVisible(box, any);
      return any;
    };
    box.style.display = 'none';
    return view;
  }

  const api = { elapsedText, tail, create };
  root.LiveView = api;
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
})(typeof globalThis !== 'undefined' ? globalThis : this);
