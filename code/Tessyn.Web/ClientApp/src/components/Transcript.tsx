import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import type { SessionEntryDto } from '../api/types';
import { formatElapsed, formatSmartTime } from '../lib/format';
import { visibleEntries } from '../lib/transcript';
import { nowMs } from '../state/appState';
import { useApp } from '../state/AppContext';
import { Markdown } from './Markdown';

const BOTTOM_THRESHOLD = 80;

function EntryView({ e, now, onFull }: { e: SessionEntryDto; now: number; onFull: (e: SessionEntryDto) => void }) {
	const time = formatSmartTime(e.timestamp, now);
	if (e.role === 'compaction') return <div className="compaction" role="separator"><span>― Context compacted above ―</span></div>;
	if (e.role === 'system') return <div className="msg system">{e.content}</div>;
	const more = e.truncated ? <button type="button" className="link-btn" onClick={() => onFull(e)}>Show full message</button> : null;
	if (e.role === 'user') {
		return (
			<div className="msg user" data-index={e.index}>
				<div className="bubble">
					<div className="meta"><span>You</span>{e.profileName && <span className="prof">{e.profileName}</span>}<span className="time">{time}</span></div>
					<div className="text">{e.content}</div>
					{more}
				</div>
			</div>
		);
	}
	const stamped = e.modelId || e.effort;
	return (
		<div className="msg assistant" data-index={e.index}>
			<div className="meta">
				{stamped && <em className="stamp">[{e.modelId ?? 'default'}, {e.effort ?? 'default'}]</em>}
				<span className="time">{time}</span>
			</div>
			<Markdown text={e.content} />
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
	}, [open.id, first, last, open.pending.length, running, t.loaded]);

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
				{entries.map((e) => <EntryView key={e.index} e={e} now={now} onFull={(x) => void actions.loadFullEntry(x)} />)}
				{open.pending.map((p) => (
					<div key={p.id} className="msg user pending" data-testid="pending">
						<div className="bubble">
							<div className="meta"><span>You</span><span className="time">{p.queued ? 'queued' : 'sending…'}</span></div>
							<div className="text">{p.text}</div>
						</div>
					</div>
				))}
				{running && open.runningSince !== null && <Thinking since={open.runningSince} />}
			</div>
			{newBelow && <button type="button" className="new-pill" onClick={toBottom}>↓ New messages</button>}
		</div>
	);
}
