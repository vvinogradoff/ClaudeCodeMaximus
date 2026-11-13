import { useLayoutEffect, useRef, useState } from 'react';
import type { SessionTagDto } from '../api/types';
import { safeHttpsUrl } from '../lib/url';
import { useApp } from '../state/AppContext';
import { Composer } from './Composer';
import { FocusView } from './FocusView';
import { KindPill } from './SessionRow';
import { Transcript } from './Transcript';

/** One horizontally scrollable row of chips; `more` fades the right edge while chips are hidden beyond it. */
function TagRow({ tags }: { tags: SessionTagDto[] }) {
	const ref = useRef<HTMLDivElement>(null);
	const [more, setMore] = useState(false);
	const update = () => {
		const el = ref.current;
		if (el) setMore(el.scrollWidth - el.clientWidth - el.scrollLeft > 2);
	};
	useLayoutEffect(() => {
		update();
		const el = ref.current;
		if (!el || typeof ResizeObserver === 'undefined') return;
		const ro = new ResizeObserver(update);
		ro.observe(el);
		return () => ro.disconnect();
	}, [tags]);
	return (
		<div ref={ref} className={`sv-tags${more ? ' more' : ''}`} onScroll={update}>
			{tags.map((t) => {
				const href = safeHttpsUrl(t.url);
				const key = `${t.kind}:${t.label}`;
				return href
					? <a key={key} className="chip static" href={href} target="_blank" rel="noopener noreferrer" title={t.label}><span className="chip-label">{t.label}</span></a>
					: <span key={key} className="chip static" title={t.label}><span className="chip-label">{t.label}</span></span>;
			})}
		</div>
	);
}

export function SessionView() {
	const { state, actions } = useApp();
	const open = state.session;

	if (!open) {
		return (
			<div className="empty-state">
				<div className="big">💬</div>
				<p>No session open</p>
				<button type="button" className="btn primary" onClick={() => actions.setPanel(true)}>Open sessions</button>
			</div>
		);
	}
	const d = open.detail;
	const att = d?.attention;
	return (
		<div className="session-view" data-testid="session-view">
			<div className="sv-header">
				{d && <div className="sv-loc">{[d.location.directoryLabel, d.location.treePath].filter((x) => x).join(' / ')}</div>}
				{!d && open.error && <div className="sv-loc">Session unavailable</div>}
				{d && d.tags.length > 0 && <TagRow tags={d.tags} />}
				<div className="seg" role="tablist">
					<button type="button" role="tab" aria-selected={open.tab === 'session'} className={open.tab === 'session' ? 'on' : ''} onClick={() => actions.setSessionTab('session')}>
						Session
					</button>
					<button type="button" role="tab" aria-selected={open.tab === 'focus'} className={open.tab === 'focus' ? 'on' : ''} onClick={() => actions.setSessionTab('focus')}>
						Focus{open.tab === 'session' && att && att.openCount > 0 && att.topKind && <KindPill kind={att.topKind} count={att.openCount} />}
					</button>
				</div>
			</div>
			{open.error && !d && <div className="empty">{open.error}</div>}
			{open.tab === 'session' ? (
				<>
					<Transcript />
					<Composer key={open.id} />
				</>
			) : <FocusView />}
		</div>
	);
}
