import { useCallback, useEffect, useRef, useState } from 'react';
import type { KeyboardEvent } from 'react';
import { readText, writeText } from '../lib/storage';
import { consumeComposerFocus } from '../state/bus';
import { useApp } from '../state/AppContext';

const MAX_HEIGHT_FRACTION = 0.4;

export function Composer() {
	const { state, actions } = useApp();
	const open = state.session!;
	const draftKey = `draft.${open.id}`;
	const [text, setText] = useState(() => readText(draftKey));
	const ref = useRef<HTMLTextAreaElement>(null);
	const running = !!open.detail?.state.running;
	const offline = !state.online;

	const resize = useCallback(() => {
		const el = ref.current;
		if (!el) return;
		el.style.height = 'auto';
		el.style.height = `${Math.min(el.scrollHeight, window.innerHeight * MAX_HEIGHT_FRACTION)}px`;
	}, []);

	useEffect(() => { resize(); }, [text, resize]);
	useEffect(() => {
		const t = window.setTimeout(() => writeText(draftKey, text), 250);
		return () => window.clearTimeout(t);
	}, [text, draftKey]);
	useEffect(() => { if (open.detail && consumeComposerFocus()) ref.current?.focus(); }, [open.detail]);

	const send = async () => {
		const value = text.trim();
		if (!value || offline) return;
		setText('');
		writeText(draftKey, '');
		const ok = await actions.send(value);
		if (!ok) setText((cur) => (cur === '' ? value : cur));
	};

	const onKey = (e: KeyboardEvent<HTMLTextAreaElement>) => {
		if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) { e.preventDefault(); void send(); }
	};

	return (
		<div className="composer">
			{offline && <div className="offline-banner" role="status">Desktop offline</div>}
			<div className="composer-row">
				<textarea ref={ref} rows={1} value={text} placeholder="Message…" disabled={offline} aria-label="Message" enterKeyHint="enter"
					onChange={(e) => setText(e.target.value)} onKeyDown={onKey} />
				{running && <button type="button" className="btn stop" onClick={() => void actions.stop()} aria-label="Stop">■ Stop</button>}
				<button type="button" className="btn primary send" disabled={offline || !text.trim()} onClick={() => void send()}>Send</button>
			</div>
		</div>
	);
}
