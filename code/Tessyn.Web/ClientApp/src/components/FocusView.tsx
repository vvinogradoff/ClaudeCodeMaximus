import { useState } from 'react';
import type { AttentionItemDto, AttentionKind } from '../api/types';
import { elevatedRunOf, outcomeLine } from '../lib/elevated';
import { formatSmartTime } from '../lib/format';
import { readText, writeText } from '../lib/storage';
import { nowMs } from '../state/appState';
import { useApp } from '../state/AppContext';
import { ElevatedFacts, ElevatedRunBody } from './ElevatedRunCard';
import { Markdown } from './Markdown';
import { RefList } from './RefList';

const KIND_LABEL: Record<AttentionKind, string> = { critical: 'CRITICAL', ask: 'ASK', todo: 'TODO', alert: 'ALERT', decided: 'DECIDED' };
const DISMISS_LABEL: Record<AttentionKind, string> = { critical: 'Got it', alert: 'Got it', decided: 'Got it', ask: 'Dismiss', todo: 'Dismiss' };

function outcome(i: AttentionItemDto): string {
	const run = elevatedRunOf(i);
	if (run && i.status === 'resolved') return outcomeLine(run) || `Resolved: ${i.resolutionNote ?? ''}`;
	if (i.status === 'answered') return `Answered: ${i.answer ?? ''}`;
	if (i.status === 'resolved') return `Resolved: ${i.resolutionNote ?? ''}`;
	return 'Dismissed';
}

function KindChip({ kind }: { kind: AttentionKind }) {
	return <span className={`chip-kind kind-${kind}`}>{KIND_LABEL[kind]}</span>;
}

interface CardProps {
	item: AttentionItemDto;
	nodeId: string;
	text: string;
	now: number;
	onText: (t: string) => void;
	onReply: () => void;
	onDismiss: () => void;
}

/** The elevated-run card (FR.23.32), read-only: the facts, the status and "Approve on the PC"; no reply box, no Reply, no Dismiss and no Run/Decline/Stop (those live on the PC). */
function ElevatedOpenCard({ item, nodeId, now }: { item: AttentionItemDto; nodeId: string; now: number }) {
	const run = elevatedRunOf(item)!;
	return (
		<article className={`card kind-border-${item.kind} elevated`} data-testid="focus-card" data-item-id={item.id} data-elevated-run={run.id}>
			<header>
				<KindChip kind={item.kind} />
				<span className="card-id">A{item.number}</span>
				<strong className="card-title">{item.title}</strong>
				<time>{formatSmartTime(item.createdUtc, now)}</time>
			</header>
			<ElevatedRunBody run={run} sessionNodeId={nodeId} now={now} />
		</article>
	);
}

function OpenCard({ item, nodeId, text, now, onText, onReply, onDismiss }: CardProps) {
	if (elevatedRunOf(item)) return <ElevatedOpenCard item={item} nodeId={nodeId} now={now} />;
	return (
		<article className={`card kind-border-${item.kind}`} data-testid="focus-card" data-item-id={item.id}>
			<header>
				<KindChip kind={item.kind} />
				<span className="card-id">A{item.number}</span>
				<strong className="card-title">{item.title}</strong>
				<time>{formatSmartTime(item.createdUtc, now)}</time>
			</header>
			{item.body && <Markdown text={item.body} className="card-body" />}
			{item.refs && item.refs.length > 0 && <RefList refs={item.refs} />}
			{item.options.length > 0 && (
				<div className="options">
					{item.options.map((o) => <button key={o} type="button" className="btn small" onClick={() => onText(o)}>{o}</button>)}
				</div>
			)}
			{item.recommendation && (
				<button type="button" className="btn small reco" onClick={() => onText(`Accepted your recommendation: ${item.recommendation}`)}>
					Use recommendation: {item.recommendation}
				</button>
			)}
			<textarea rows={2} placeholder="Reply..." value={text} onChange={(e) => onText(e.target.value)} aria-label={`Reply to A${item.number}`} />
			<div className="card-actions">
				<button type="button" className="btn primary" disabled={!text.trim()} onClick={onReply}>Reply</button>
				<span className="spacer" />
				<button type="button" className="btn" onClick={onDismiss}>{DISMISS_LABEL[item.kind]}</button>
			</div>
		</article>
	);
}

function ClosedCard({ item, nodeId }: { item: AttentionItemDto; nodeId: string }) {
	const [open, setOpen] = useState(false);
	const run = elevatedRunOf(item);
	return (
		<article className="card closed" onClick={() => setOpen((o) => !o)} data-testid="closed-card">
			<header>
				<KindChip kind={item.kind} />
				<span className="card-id">A{item.number}</span>
				<strong className="card-title">{item.title}</strong>
			</header>
			<div className="outcome">{outcome(item)}</div>
			{open && run && <ElevatedFacts run={run} sessionNodeId={nodeId} />}
			{open && !run && item.body && <Markdown text={item.body} className="card-body" />}
			{item.refs && item.refs.length > 0 && <RefList refs={item.refs} />}
		</article>
	);
}

export function FocusView() {
	const { state, actions } = useApp();
	const open = state.session!;
	const nodeId = open.id;
	const items = open.attention?.items ?? [];
	const [texts, setTexts] = useState<Record<string, string>>({});
	const [showDecisions, setShowDecisions] = useState(false);
	const [showClosed, setShowClosed] = useState(false);
	const now = nowMs(state);

	const textOf = (id: string) => texts[id] ?? readText(`reply.${nodeId}.${id}`);
	const setText = (id: string, v: string) => {
		setTexts((t) => ({ ...t, [id]: v }));
		writeText(`reply.${nodeId}.${id}`, v);
	};

	const openItems = items.filter((i) => i.status === 'open');
	const actionable = openItems.filter((i) => i.kind !== 'decided');
	const decisions = openItems.filter((i) => i.kind === 'decided');
	const closed = items.filter((i) => i.status !== 'open');
	const withText = openItems.filter((i) => textOf(i.id).trim());

	if (!open.attention) return <div className="focus"><div className="empty">Loading…</div></div>;

	const card = (i: AttentionItemDto) => (
		<OpenCard key={i.id} item={i} nodeId={nodeId} text={textOf(i.id)} now={now} onText={(v) => setText(i.id, v)}
			onReply={() => void actions.replyAttention([{ itemId: i.id, text: textOf(i.id).trim() }])} onDismiss={() => void actions.dismissAttention(i.id)} />
	);

	return (
		<div className="focus" data-testid="focus">
			{items.length === 0 && <div className="empty">Nothing needs your attention in this session.</div>}
			{items.length > 0 && actionable.length === 0 && decisions.length === 0 && <div className="empty">Nothing needs your attention in this session.</div>}
			{actionable.map(card)}
			{decisions.length > 0 && (
				<>
					<button type="button" className="expander" aria-expanded={showDecisions} onClick={() => setShowDecisions((v) => !v)}>
						{showDecisions ? '⌄' : '›'} Decisions ({decisions.length})
					</button>
					{showDecisions && decisions.map(card)}
				</>
			)}
			{closed.length > 0 && (
				<>
					<button type="button" className="expander" aria-expanded={showClosed} onClick={() => setShowClosed((v) => !v)}>
						{showClosed ? '⌄' : '›'} Closed ({closed.length})
					</button>
					{showClosed && closed.map((i) => <ClosedCard key={i.id} item={i} nodeId={nodeId} />)}
				</>
			)}
			{withText.length > 0 && (
				<div className="send-all">
					<button type="button" className="btn primary" onClick={() => void actions.replyAttention(withText.map((i) => ({ itemId: i.id, text: textOf(i.id).trim() })))}>
						Send answers ({withText.length})
					</button>
				</div>
			)}
		</div>
	);
}
