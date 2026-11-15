import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import type { SessionEntryDto } from '../api/types';
import { formatElapsed, formatSmartTime } from '../lib/format';
import { findQuoteRange, quoteOf } from '../lib/highlight';
import { inputLabel, isAutomatedInput } from '../lib/inputSource';
import { visibleEntries } from '../lib/transcript';
import { nowMs } from '../state/appState';
import type { FocusEntry } from '../state/appState';
import { setViewportProbe } from '../state/bus';
import { useApp } from '../state/AppContext';
import { EntryExtras } from './EntryExtras';
import { Markdown } from './Markdown';

const BOTTOM_THRESHOLD = 80;
const HIGHLIGHT_NAME = 'ref-hit';

interface HighlightRegistry { set(name: string, h: unknown): void; get(name: string): unknown; delete(name: string): void }
const registry = (): HighlightRegistry | undefined => (globalThis as unknown as { CSS?: { highlights?: HighlightRegistry } }).CSS?.highlights;

/** User entries are plain text: split at the exact offsets the daemon gave. Out-of-range offsets show the text unmarked. */
function MarkedText({ text, mark }: { text: string; mark?: FocusEntry }) {
	if (!mark?.mark || mark.offset === undefined || mark.length === undefined) return <>{text}</>;
	const q = quoteOf(text, mark.offset, mark.length);
	if (!q) return <>{text}</>;
	return <>{text.slice(0, mark.offset)}<mark className="ref-mark" data-testid="ref-mark">{q}</mark>{text.slice(mark.offset + q.length)}</>;
}

export function EntryView({ e, now, onFull, focus }: { e: SessionEntryDto; now: number; onFull: (e: SessionEntryDto) => void; focus?: FocusEntry }) {
	const time = formatSmartTime(e.timestamp, now);
	const body = useRef<HTMLDivElement>(null);
	const marked = !!focus?.mark;

	// Assistant entries are rendered markdown: find the quote in the rendered text and mark it with the CSS Custom Highlight API.
	useEffect(() => {
		const reg = registry();
		const Hl = (globalThis as unknown as { Highlight?: new (...r: Range[]) => unknown }).Highlight;
		if (!focus?.mark || e.role !== 'assistant' || !body.current || !reg || !Hl || focus.offset === undefined || focus.length === undefined) return;
		const q = quoteOf(e.content, focus.offset, focus.length);
		const range = q ? findQuoteRange(body.current, q) : null;
		if (!range) return;
		const mine = new Hl(range);
		reg.set(HIGHLIGHT_NAME, mine);
		// Only remove the highlight if it is still this entry's (another jump may have replaced it already).
		return () => { if (reg.get(HIGHLIGHT_NAME) === mine) reg.delete(HIGHLIGHT_NAME); };
	}, [focus, e.content, e.role]);

	if (e.role === 'compaction') return <div className="compaction" role="separator"><span>― Context compacted above ―</span></div>;
	if (e.role === 'system') return <div className="msg system">{e.content}</div>;
	const more = e.truncated ? <button type="button" className="link-btn" onClick={() => onFull(e)}>Show full message</button> : null;
	if (e.role === 'user') {
		return (
			<div className={`msg user${isAutomatedInput(e.source) ? ' auto' : ''}${marked ? ' hl-entry' : ''}`} data-index={e.index} data-entry-id={e.entryId}>
				<div className="bubble">
					<div className="meta"><span>{inputLabel(e.source)}</span>{e.profileName && <span className="prof">{e.profileName}</span>}<span className="time">{time}</span></div>
					<div className="text"><MarkedText text={e.content} mark={focus} /></div>
					{more}
				</div>
			</div>
		);
	}
	const stamped = e.modelId || e.effort;
	return (
		<div className={`msg assistant${marked ? ' hl-entry' : ''}`} data-index={e.index} data-entry-id={e.entryId}>
			<div className="meta">
				{stamped && <em className="stamp">[{e.modelId ?? 'default'}, {e.effort ?? 'default'}]</em>}
				<span className="time">{time}</span>
			</div>
			<div ref={body}><Markdown text={e.content} /></div>
			<EntryExtras entry={e} />
			{more}
		</div>
	);
}

function Thinking({ since }: { since: number }) {
	const [, setN] = useState(0);
	useEffect(() => {
		const t = window.setInterval(() => setN((n) => n + 1), 1000);
		return () => window.clearInterval(t);
	}, []);
	return <div className="thinking" data-testid="thinking"><span className="dots" />Claude is thinking… {formatElapsed(Date.now() - since)}</div>;
}

export function Transcript() {
	const { state, actions } = useApp();
	const open = state.session!;
	const t = open.transcript;
	const ref = useRef<HTMLDivElement>(null);
	const atBottom = useRef(true);
	const prev = useRef({ id: '', first: -1, last: -1, height: 0, pending: 0 });
	const handledFocus = useRef(0);
	const [newBelow, setNewBelow] = useState(false);
	const now = nowMs(state);

	const entries = visibleEntries(t.entries);
	const first = t.entries.length > 0 ? t.entries[0].index : -1;
	const last = t.entries.length > 0 ? t.entries[t.entries.length - 1].index : -1;
	const running = !!open.detail?.state.running;

	useLayoutEffect(() => {
		const el = ref.current;
		if (!el) return;
		const p = prev.current;
		const sameSession = p.id === open.id && p.last >= 0;
		const fe = open.focusEntry;
		if (fe && handledFocus.current !== fe.token) {
			// Jumped here from a Focus ref (or Back): bring that entry into view instead of scrolling to the bottom.
			const target = Array.from(el.querySelectorAll<HTMLElement>('[data-entry-id]')).find((n) => n.dataset.entryId === fe.entryId);
			if (target) {
				handledFocus.current = fe.token;
				const er = el.getBoundingClientRect();
				const tr = target.getBoundingClientRect();
				const centre = fe.mark ? Math.max(0, (el.clientHeight - Math.min(tr.height, el.clientHeight)) / 2) : 8;
				el.scrollTop += tr.top - er.top - centre;
				atBottom.current = false;
				setNewBelow(false);
				prev.current = { id: open.id, first, last, height: el.scrollHeight, pending: open.pending.length };
				return;
			}
		}
		if (!sameSession) {
			el.scrollTop = el.scrollHeight;
			atBottom.current = true;
			setNewBelow(false);
		} else if (first < p.first && last === p.last) {
			// Earlier entries were prepended: keep the viewport anchored.
			el.scrollTop += el.scrollHeight - p.height;
		} else if (last !== p.last || open.pending.length !== p.pending || running) {
			if (atBottom.current) el.scrollTop = el.scrollHeight;
			else if (last !== p.last) setNewBelow(true);
		}
		prev.current = { id: open.id, first, last, height: el.scrollHeight, pending: open.pending.length };
	}, [open.id, first, last, open.pending.length, running, t.loaded, open.focusEntry]);

	// Lets a jump remember where the user is (the entry at the top of the viewport) so Back can return there.
	useEffect(() => {
		setViewportProbe(() => {
			const el = ref.current;
			let firstVisibleEntryId: string | null = null;
			if (el) {
				const top = el.getBoundingClientRect().top + 8;
				for (const n of Array.from(el.querySelectorAll<HTMLElement>('[data-entry-id]'))) {
					if (n.getBoundingClientRect().bottom > top) { firstVisibleEntryId = n.dataset.entryId ?? null; break; }
				}
			}
			return { firstVisibleEntryId, atBottom: atBottom.current };
		});
		return () => setViewportProbe(null);
	}, []);

	const onScroll = () => {
		const el = ref.current!;
		atBottom.current = el.scrollHeight - el.scrollTop - el.clientHeight < BOTTOM_THRESHOLD;
		if (atBottom.current) setNewBelow(false);
	};

	const toBottom = () => {
		const el = ref.current!;
		el.scrollTo({ top: el.scrollHeight, behavior: 'smooth' });
		setNewBelow(false);
	};

	return (
		<div className="transcript-wrap">
			<div className="transcript" ref={ref} onScroll={onScroll} data-testid="transcript">
				{t.hasEarlier && <button type="button" className="btn small load-earlier" onClick={() => void actions.loadEarlier()}>Load earlier</button>}
				{!t.loaded && !open.error && <div className="empty">Loading…</div>}
				{t.loaded && entries.length === 0 && open.pending.length === 0 && <div className="empty">No messages yet. Say hello below.</div>}
				{entries.map((e) => (
					<EntryView key={e.index} e={e} now={now} onFull={(x) => void actions.loadFullEntry(x)}
						focus={open.focusEntry && e.entryId === open.focusEntry.entryId ? open.focusEntry : undefined} />
				))}
				{t.anchored && (
					<div className="anchored-bar" data-testid="anchored-bar">
						<button type="button" className="btn small" onClick={() => void actions.loadLater()}>Load later</button>
						<button type="button" className="btn small" onClick={() => void actions.jumpToLatest()}>Jump to latest</button>
					</div>
				)}
				{!t.anchored && open.pending.map((p) => (
					<div key={p.id} className="msg user pending" data-testid="pending">
						<div className="bubble">
							<div className="meta"><span>You</span><span className="time">{p.queued ? 'queued' : 'sending…'}</span></div>
							<div className="text">{p.text}</div>
						</div>
					</div>
				))}
				{!t.anchored && running && open.runningSince !== null && <Thinking since={open.runningSince} />}
			</div>
			{newBelow && <button type="button" className="new-pill" onClick={toBottom}>↓ New messages</button>}
		</div>
	);
}
