import type { ReactNode } from 'react';
import type { AttentionKind, SessionStateDto } from '../api/types';
import { formatDateTime } from '../lib/format';
import { isRecentlyActive, recencyBand } from '../lib/recency';

export function KindPill({ kind, count }: { kind: AttentionKind; count: number }) {
	return <span className={`pill kind-${kind}`} title={`${count} open (${kind})`}>{count}</span>;
}

/** FR.1.28: the workers running now (direct and nested) as a red number in a dark-red outlined square; nothing at 0 or when absent. */
export function ActiveWorkersBadge({ count }: { count?: number }) {
	if (!count || count <= 0) return null;
	return <span className="workers-badge" data-testid="active-workers" title={`Workers running now: ${count}`} aria-label={`Workers running now: ${count}`}>{count}</span>;
}

export function StatusBlock({ running }: { running: boolean }) {
	return <span className="status-block" aria-hidden="true"><i className={running ? 'run' : ''} /></span>;
}

interface Props {
	name: string;
	state: SessionStateDto;
	nowMs: number;
	selected?: boolean;
	indent?: number;
	profileName?: string;
	/** Small text shown before the profile on line 2 (e.g. directory label in Recent). */
	label?: string;
	showTime?: boolean;
	hasSchedules?: boolean;
	trailing?: ReactNode;
	line2Right?: ReactNode;
	onClick?: () => void;
}

/** The shared session row (tree, recent, tags): status block, attention pill, name, draft, favourite; recency shading. */
export function SessionRow(p: Props) {
	const band = recencyBand(p.state.lastActivityUtc, p.nowMs);
	const active = isRecentlyActive(p.state.lastActivityUtc, p.nowMs);
	const cls = ['srow', `rec-${band}`, active ? 'active24' : '', p.selected ? 'selected' : ''].filter(Boolean).join(' ');
	const profile = p.profileName ?? p.state.profileName;
	return (
		<div className={cls} style={p.indent ? { marginLeft: p.indent * 14 } : undefined} role="button" tabIndex={0}
			onClick={p.onClick} onKeyDown={(e) => { if (e.key === 'Enter') p.onClick?.(); }} data-session-id={p.state.id}>
			<div className="srow-l1">
				<StatusBlock running={p.state.running} />
				{p.state.attention && <KindPill kind={p.state.attention.topKind} count={p.state.attention.count} />}
				<span className="srow-name">{p.name}</span>
				{p.state.hasDraft && <span className="draft" title="Has a draft">✏</span>}
				{p.hasSchedules && <span className="sched" title="Has schedules">🕒</span>}
				<ActiveWorkersBadge count={p.state.runningWorkerCount} />
				<span className="srow-trailing">{p.trailing ?? (p.state.favorite ? <span className="star" title="Favourite">★</span> : null)}</span>
			</div>
			<div className="srow-l2">
				<span className="srow-profile">{p.label ? `${p.label}${profile ? ' · ' : ''}` : ''}{profile ?? ''}</span>
				<span className="srow-time">{p.line2Right ?? (p.showTime === false ? '' : formatDateTime(p.state.lastPromptUtc))}</span>
			</div>
		</div>
	);
}
