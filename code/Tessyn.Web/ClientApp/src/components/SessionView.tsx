import { safeHttpsUrl } from '../lib/url';
import { useApp } from '../state/AppContext';
import { Composer } from './Composer';
import { FocusView } from './FocusView';
import { KindPill } from './SessionRow';
import { Transcript } from './Transcript';

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
				{d && <div className="sv-loc">{d.location.directoryLabel} / {d.location.treePath}</div>}
				<div className="sv-name">{d?.name ?? (open.error ? 'Session unavailable' : 'Loading…')}</div>
				{d && d.tags.length > 0 && (
					<div className="sv-tags">
						{d.tags.map((t) => {
							const href = safeHttpsUrl(t.url);
							const key = `${t.kind}:${t.label}`;
							return href
								? <a key={key} className="chip static" href={href} target="_blank" rel="noopener noreferrer">{t.label}</a>
								: <span key={key} className="chip static">{t.label}</span>;
						})}
					</div>
				)}
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
