// Copy as Markdown (FR.2.13): turns a rendered fragment of the transcript back into Markdown. No dependency, no page state. It reads only the small
// DOM interface a browser node and the test suite's mini DOM share (nodeType, nodeName, nodeValue, childNodes, getAttribute), so it runs under node
// in the test suite (MdCopyScriptTests) and in the page (the context menu and Ctrl+Shift+C of transcript.js).
//
// What the host's renderer (MarkdigEntryRenderer + the sanitizer) can produce is all that has to be understood: headings, paragraphs, lists, quotes,
// code blocks, tables, rules (hr, and hr.dbl = the double rule of FR.2.12), emphasis, inline code, links and line breaks. The page adds path chips
// (span.pth: copied as their text), image slots (span.pslot > img.pimg, written inline right after the path) and media previews (div.mp > div.ms, one
// paragraph each); what an image or video stands for is not in the DOM (an image shows a cache URL, a not yet loaded preview carries no address), so the
// page passes a `ctx.media(el)` that returns the { url, alt, video } of such an img or div.ms, and the converter writes ![alt](url), or [alt](url) for a video.
'use strict';
(function (root) {
  const HEADING = /^h([1-6])$/;
  const SKIP_CLASS = ['h', 't', 'ts', 'more', 'pnote', 'mfail', 'mload', 'pplay'];   // row chrome and hints: header lines, the time, "Show full entry", "Loading image..."
  const RULE = '---', DOUBLE_RULE = '===';
  const BR = '\u0001';   // a line break while converting (written as two spaces and a newline at the end, so that tidying up does not strip them)

  function name(n) { return String(n.nodeName || '').toLowerCase(); }
  function classes(n) { return n.nodeType === 1 ? String(n.getAttribute('class') || '').split(/\s+/).filter(Boolean) : []; }
  function hasClass(n, c) { return classes(n).indexOf(c) >= 0; }
  function kids(n) { return Array.prototype.slice.call(n.childNodes || []); }
  function textOf(n) {
    if (n.nodeType === 3) return n.nodeValue || '';
    let s = ''; for (const c of kids(n)) s += textOf(c); return s;
  }
  function firstTag(n, tag) { for (const c of kids(n)) { if (c.nodeType !== 1) continue; if (name(c) === tag) return c; const d = firstTag(c, tag); if (d) return d; } return null; }
  function skipped(n) { return n.nodeType === 1 && classes(n).some(c => SKIP_CLASS.indexOf(c) >= 0); }

  // ---- inline ----
  function codeSpan(text) {
    const runs = text.match(/`+/g) || [];
    let n = 1; while (runs.indexOf('`'.repeat(n)) >= 0) n++;
    const fence = '`'.repeat(n), pad = /^`|`$/.test(text) || /^ .* $/.test(text) ? ' ' : '';
    return fence + pad + text + pad + fence;
  }
  function imageMd(m) {
    const url = /[\s()<>]/.test(m.url) ? '<' + m.url + '>' : m.url, alt = String(m.alt || '').replace(/[\[\]]/g, '');
    return (m.video ? '[' : '![') + alt + '](' + url + ')';
  }
  function inline(parent, ctx, state) {
    let s = '';
    for (const n of kids(parent)) s += inlineNode(n, ctx, state);
    return s;
  }
  function wrapMark(mark, inner) {   // emphasis marks hug the text: "** a **" is not bold in Markdown
    const lead = inner.match(/^\s*/)[0], trail = inner.match(/\s*$/)[0], core = inner.trim();
    return core ? lead + mark + core + mark + trail : inner;
  }
  function inlineNode(n, ctx, state) {
    if (n.nodeType === 3) return (n.nodeValue || '').replace(/\s+/g, ' ');
    if (n.nodeType !== 1 || skipped(n)) return '';
    const t = name(n);
    if (hasClass(n, 'pslot')) { const img = firstTag(n, 'img'), m = img && ctx && ctx.media ? ctx.media(img) : null; return m ? ' ' + imageMd(m) : ''; }   // the image under a path, kept right after it
    switch (t) {
      case 'br': return BR;
      case 'strong': case 'b': return wrapMark('**', inline(n, ctx, state));
      case 'em': case 'i': return wrapMark('*', inline(n, ctx, state));
      case 'del': case 's': return wrapMark('~~', inline(n, ctx, state));
      case 'code': return codeSpan(textOf(n));
      case 'a': {
        const href = n.getAttribute('href') || '', label = inline(n, ctx, state);
        if (!href || href === '#' || hasClass(n, 'blocked')) return label;   // a link the policy refused is plain text
        if (label.trim() === href) return href;                               // an autolink
        return '[' + label + '](' + (/[\s()]/.test(href) ? '<' + href + '>' : href) + ')';
      }
      case 'img': { const m = ctx && ctx.media ? ctx.media(n) : null; return m ? imageMd(m) : ''; }
      case 'video': { const m = ctx && ctx.media ? ctx.media(n) : null; return m ? imageMd(m) : ''; }
      default: return inline(n, ctx, state);   // span.pth (a path chip), mark (a find highlight), sub, sup, ins ...: their text
    }
  }

  // ---- blocks ----
  const BLOCK = { p: 1, ul: 1, ol: 1, blockquote: 1, pre: 1, hr: 1, table: 1, div: 1, h1: 1, h2: 1, h3: 1, h4: 1, h5: 1, h6: 1, li: 1, thead: 1, tbody: 1, tr: 1 };
  function isBlock(n) { return n.nodeType === 1 && (BLOCK[name(n)] === 1 || HEADING.test(name(n))); }

  // The blocks inside a container: loose inline content between blocks becomes a paragraph of its own. Returns [{ kind, text }].
  function blocks(parent, ctx, state) {
    const out = []; let run = '';
    const flush = () => { const t = run.replace(/^\s+|\s+$/g, ''); if (t) out.push({ kind: 'p', text: tidy(t) }); run = ''; };
    for (const n of kids(parent)) {
      if (n.nodeType === 1 && skipped(n)) continue;
      if (n.nodeType === 1 && hasClass(n, 'mp')) { flush(); for (const ms of kids(n)) { const m = ctx && ctx.media ? ctx.media(ms) : null; if (m) out.push({ kind: 'p', text: imageMd(m) }); } continue; }
      if (!isBlock(n)) { run += inlineNode(n, ctx, state); continue; }
      flush();
      for (const b of block(n, ctx, state)) out.push(b);
    }
    flush();
    return out;
  }
  function tidy(t) { return t.replace(/^\u0001+|\u0001+$/g, '').replace(/\u0001\s*/g, BR).replace(/\n{3,}/g, '\n\n'); }
  function flat(t) { return t.replace(/\u0001/g, ' ').replace(/\s+/g, ' ').trim(); }   // headings and table cells have no line breaks
  function join(bs, sep) { return bs.map(b => b.text).join(sep); }
  function indent(text, pad, first) { return text.split('\n').map((l, i) => (i === 0 ? first : (l ? pad : ''))  + l).join('\n'); }

  function block(n, ctx, state) {
    const t = name(n), h = HEADING.exec(t);
    if (h) { const text = flat(inline(n, ctx, state)); return text ? [{ kind: 'h', text: '#'.repeat(parseInt(h[1], 10)) + ' ' + text }] : []; }
    switch (t) {
      case 'p': { const text = tidy(inline(n, ctx, state).replace(/^\s+|\s+$/g, '')); return text ? [{ kind: 'p', text }] : []; }
      case 'hr': return [{ kind: 'hr', text: hasClass(n, 'dbl') ? DOUBLE_RULE : RULE }];
      case 'pre': return [{ kind: 'pre', text: fence(n) }];
      case 'blockquote': {
        const inner = join(blocks(n, ctx, state), '\n\n');
        return inner ? [{ kind: 'q', text: inner.split('\n').map(l => l ? '> ' + l : '>').join('\n') }] : [];
      }
      case 'ul': case 'ol': return list(n, ctx, state);
      case 'table': return table(n, ctx, state);
      case 'li': return blocks(n, ctx, state);   // a list item that lost its list (a partial selection)
      default: return blocks(n, ctx, state);     // div, thead, tbody, tr ...: their blocks
    }
  }

  function fence(pre) {
    const code = kids(pre).find(c => c.nodeType === 1 && name(c) === 'code'), lang = (code && classes(code).map(c => /^language-(.+)$/.exec(c)).find(Boolean) || [])[1] || '';
    const body = textOf(pre).replace(/\n$/, '');
    const runs = body.match(/`{3,}/g) || []; let n = 3; for (const r of runs) if (r.length >= n) n = r.length + 1;
    const f = '`'.repeat(n);
    return f + lang + '\n' + body + '\n' + f;
  }

  function list(n, ctx, state) {
    const ordered = name(n) === 'ol', start = parseInt(n.getAttribute('start') || '1', 10);
    const items = kids(n).filter(c => c.nodeType === 1 && name(c) === 'li');
    const rendered = items.map(li => blocks(li, ctx, state));
    const loose = items.some(hasParagraphElement);   // Markdig writes <p> inside an item only for a loose list
    const out = rendered.map((bs, k) => {
      const marker = ordered ? (isNaN(start) ? 1 : start) + k + '. ' : '- ';
      const body = join(bs, loose ? '\n\n' : '\n');
      return indent(body, ' '.repeat(marker.length), marker).replace(/ +$/, '');
    });
    return out.length ? [{ kind: 'list', text: out.join(loose ? '\n\n' : '\n') }] : [];
  }
  function hasParagraphElement(li) { return !!li && kids(li).some(c => c.nodeType === 1 && name(c) === 'p'); }

  function table(n, ctx, state) {
    const rows = [];
    (function collect(p) { for (const c of kids(p)) { if (c.nodeType !== 1) continue; if (name(c) === 'tr') rows.push(c); else collect(c); } })(n);
    if (!rows.length) return [];
    const cellsOf = tr => kids(tr).filter(c => c.nodeType === 1 && /^(th|td)$/.test(name(c)));
    const cell = c => flat(inline(c, ctx, state)).replace(/\|/g, '\\|');
    const head = cellsOf(rows[0]), width = head.length;
    const line = cells => '| ' + cells.join(' | ') + ' |';
    const align = head.map(c => hasClass(c, 'al-center') ? ':---:' : hasClass(c, 'al-right') ? '---:' : hasClass(c, 'al-left') ? ':---' : '---');
    const out = [line(head.map(cell)), line(align)];
    for (const tr of rows.slice(1)) { const cs = cellsOf(tr).map(cell); while (cs.length < width) cs.push(''); out.push(line(cs.slice(0, Math.max(width, cs.length)))); }
    return [{ kind: 'table', text: out.join('\n') }];
  }

  // The Markdown of a node (an element, or a document fragment holding the cloned contents of a selection).
  // ctx.media(el) -> { url, alt, video } | null  describes an image or video element of the page.
  function fromNode(node, ctx) {
    const state = {};
    const bs = node.nodeType === 1 && isBlock(node) ? block(node, ctx, state) : blocks(node, ctx, state);   // a fragment (nodeType 11) or an inline element: its content
    return join(bs, '\n\n').replace(/^\s+|\s+$/g, '').replace(/\u0001/g, '  \n');
  }

  // The parts of a copy joined into one text: a blank line between entries, empty parts dropped.
  function joinParts(parts) { return parts.map(p => String(p || '').replace(/^\s+|\s+$/g, '')).filter(Boolean).join('\n\n'); }

  const api = { fromNode, joinParts, codeSpan, imageMd };
  root.MdCopy = api;
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
})(typeof globalThis !== 'undefined' ? globalThis : this);
