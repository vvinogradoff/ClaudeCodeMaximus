import { useCallback, useEffect, useRef, useState } from 'react';
import type { ChangeEvent, KeyboardEvent } from 'react';
import { getTransport, UploadError } from '../api/rpc';
import type { UploadHandle } from '../api/rpc';
import { attachmentProblem, insertAttachedLine } from '../lib/attach';
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

	// ---- attachments -------------------------------------------------------------------------------------
	const fileInput = useRef<HTMLInputElement>(null);
	const upload = useRef<UploadHandle | null>(null);
	const alive = useRef(true);
	const caretAfter = useRef<number | null>(null);
	const [uploading, setUploading] = useState<{ name: string; fraction: number; index: number; count: number } | null>(null);
	const [attachError, setAttachError] = useState<string | null>(null);
	useEffect(() => { alive.current = true; return () => { alive.current = false; upload.current?.abort(); }; }, []);

	const insertPath = (path: string) => {
		const el = ref.current;
		const caretStart = el?.selectionStart ?? text.length;
		const caretEnd = el?.selectionEnd ?? text.length;
		setText((cur) => {
			const r = insertAttachedLine(cur, caretStart, caretEnd, path);
			caretAfter.current = r.caret;
			return r.text;
		});
	};
	useEffect(() => {
		const c = caretAfter.current;
		if (c === null || !ref.current) return;
		caretAfter.current = null;
		ref.current.focus();
		ref.current.setSelectionRange(c, c);
	}, [text]);

	const attach = async (files: File[]) => {
		setAttachError(null);
		for (let i = 0; i < files.length; i++) {
			const file = files[i];
			const problem = attachmentProblem(file);
			if (problem) { setAttachError(problem); return; }
			setUploading({ name: file.name, fraction: 0, index: i + 1, count: files.length });
			const handle = getTransport().upload(open.id, file, (fraction) => { if (alive.current) setUploading((u) => (u ? { ...u, fraction } : u)); });
			upload.current = handle;
			try {
				const r = await handle.promise;
				if (!alive.current) return;
				insertPath(r.path);
			} catch (e) {
				if (alive.current) {
					if (!(e instanceof UploadError && e.status === -1)) setAttachError(`${file.name}: ${e instanceof Error ? e.message : String(e)}`);
					setUploading(null);
				}
				return;
			} finally { upload.current = null; }
		}
		if (alive.current) setUploading(null);
	};

	const onPick = (e: ChangeEvent<HTMLInputElement>) => {
		const files = Array.from(e.target.files ?? []);
		e.target.value = ''; // the same file can be picked again
		if (files.length > 0) void attach(files);
	};

	const onKey = (e: KeyboardEvent<HTMLTextAreaElement>) => {
		if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) { e.preventDefault(); void send(); }
	};

	return (
		<div className="composer">
			{offline && <div className="offline-banner" role="status">Desktop offline</div>}
			{uploading && (
				<div className="upload-row" data-testid="upload-progress">
					<span className="upload-name">Uploading {uploading.count > 1 ? `${uploading.index}/${uploading.count} ` : ''}{uploading.name}</span>
					<div className="upload-bar" role="progressbar" aria-label={`Uploading ${uploading.name}`} aria-valuemin={0} aria-valuemax={100} aria-valuenow={Math.round(uploading.fraction * 100)}>
						<i style={{ width: `${Math.round(uploading.fraction * 100)}%` }} />
					</div>
					<span className="upload-pct">{Math.round(uploading.fraction * 100)}%</span>
					<button type="button" className="btn small" onClick={() => upload.current?.abort()}>Cancel</button>
				</div>
			)}
			{attachError && (
				<div className="attach-error" role="alert" data-testid="attach-error">
					<span>{attachError}</span>
					<button type="button" className="link-btn" onClick={() => setAttachError(null)} aria-label="Dismiss error">Dismiss</button>
				</div>
			)}
			<div className="composer-row">
				<input ref={fileInput} type="file" multiple hidden onChange={onPick} data-testid="attach-input" tabIndex={-1} aria-hidden="true" />
				<button type="button" className="btn attach" aria-label="Attach file" title="Attach file" disabled={offline || !!uploading} onClick={() => fileInput.current?.click()}>
					<span aria-hidden="true">📎</span>
				</button>
				<textarea ref={ref} rows={1} value={text} placeholder="Message…" disabled={offline} aria-label="Message" enterKeyHint="enter"
					onChange={(e) => setText(e.target.value)} onKeyDown={onKey} />
				{running && <button type="button" className="btn stop" onClick={() => void actions.stop()} aria-label="Stop">■ Stop</button>}
				<button type="button" className="btn primary send" disabled={offline || !text.trim()} onClick={() => void send()}>Send</button>
			</div>
		</div>
	);
}
