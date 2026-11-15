import { useEffect, useMemo, useRef, useState } from 'react';
import { rpc } from '../api/rpc';
import type { FileReadResult } from '../api/types';
import { readBlob, ReadCancelled, readText } from '../lib/fileRead';
import { blobMime, classifyPreview, formatBytes, PREVIEW_LIMITS } from '../lib/media';
import type { PreviewKind } from '../lib/media';
import { renderFileMarkdown } from '../lib/markdown';
import { currentOpener, trackActivations } from '../lib/opener';
import type { PreviewTarget } from '../state/appState';

trackActivations();

type View =
	| { phase: 'loading'; loaded: number; total: number }
	| { phase: 'text'; text: string }
	| { phase: 'blob'; url: string; blob: Blob }
	| { phase: 'idle' } // pdf: loaded only when asked
	| { phase: 'tooLarge'; limit: number }
	| { phase: 'unsupported' }
	| { phase: 'error'; message: string };

const readChunk = (p: { handle: string; offset: number }) => rpc<FileReadResult>('file.read', p);

function ImageView({ url, blob, name }: { url: string; blob: Blob; name: string }) {
	// The server's Content-Security-Policy may not allow blob: images; a data: URL is allowed, so retry once with that.
	const [src, setSrc] = useState(url);
	const [failed, setFailed] = useState(false);
	const onError = () => {
		if (src !== url) { setFailed(true); return; }
		const fr = new FileReader();
		fr.onload = () => setSrc(String(fr.result));
		fr.onerror = () => setFailed(true);
		fr.readAsDataURL(blob);
	};
	if (failed) return <p className="pv-note" role="status">This image could not be displayed.</p>;
	return <img className="pv-img" src={src} alt={name} onError={onError} />;
}

function VideoView({ url }: { url: string }) {
	const [failed, setFailed] = useState(false);
	if (failed) return <p className="pv-note" role="status">This video could not be played here. Use Download instead.</p>;
	return <video className="pv-video" src={url} controls playsInline preload="metadata" onError={() => setFailed(true)} />;
}

function focusables(root: HTMLElement): HTMLElement[] {
	return Array.from(root.querySelectorAll<HTMLElement>('button:not([disabled]), a[href], [tabindex]:not([tabindex="-1"])'));
}

/** Bottom sheet that previews one file read through file.read: markdown/text/code, image, video; pdf as download or new tab. */
export function PreviewSheet({ target, onClose }: { target: PreviewTarget; onClose: () => void }) {
	const kind: PreviewKind = useMemo(() => classifyPreview(target.name, target.mime), [target.name, target.mime]);
	const [view, setView] = useState<View>({ phase: 'loading', loaded: 0, total: target.size });
	const [source, setSource] = useState(false);
	const [wantPdf, setWantPdf] = useState(false);
	const sheet = useRef<HTMLDivElement>(null);
	const closeBtn = useRef<HTMLButtonElement>(null);
	const titleId = 'pv-title';

	// Focus management: move focus into the sheet, give it back to the opener on close.
	useEffect(() => {
		const opener = currentOpener();
		closeBtn.current?.focus();
		return () => { if (opener?.isConnected) opener.focus(); };
	}, []);

	useEffect(() => {
		setSource(false);
		setWantPdf(false);
	}, [target.handle]);

	useEffect(() => {
		let cancelled = false;
		let url: string | null = null;
		const ctl = { cancelled: () => cancelled, onProgress: (loaded: number, total: number) => { if (!cancelled) setView({ phase: 'loading', loaded, total }); } };
		const run = async () => {
			if (kind === 'other') { setView({ phase: 'unsupported' }); return; }
			const limit = PREVIEW_LIMITS[kind];
			if (target.size > limit) { setView({ phase: 'tooLarge', limit }); return; }
			if (kind === 'pdf' && !wantPdf) { setView({ phase: 'idle' }); return; }
			setView({ phase: 'loading', loaded: 0, total: target.size });
			try {
				if (kind === 'markdown' || kind === 'text') {
					const text = await readText(readChunk, target.handle, target.size, limit, ctl);
					if (!cancelled) setView({ phase: 'text', text });
				} else {
					const blob = await readBlob(readChunk, target.handle, target.size, limit, blobMime(target.name, target.mime, kind), ctl);
					if (cancelled) return;
					url = URL.createObjectURL(blob);
					setView({ phase: 'blob', url, blob });
				}
			} catch (e) {
				if (e instanceof ReadCancelled || cancelled) return;
				setView({ phase: 'error', message: e instanceof Error ? e.message : String(e) });
			}
		};
		void run();
		return () => { cancelled = true; if (url) URL.revokeObjectURL(url); };
	}, [target.handle, target.name, target.mime, target.size, kind, wantPdf]);

	// Window-level, not on the sheet: Safari does not focus a button on click, so keys would otherwise go to the page behind.
	useEffect(() => {
		const onKey = (e: globalThis.KeyboardEvent) => {
			if (e.key === 'Escape') { e.stopPropagation(); onClose(); return; }
			if (e.key !== 'Tab' || !sheet.current) return;
			const f = focusables(sheet.current);
			if (f.length === 0) return;
			const first = f[0];
			const last = f[f.length - 1];
			const active = document.activeElement;
			if (!sheet.current.contains(active)) { e.preventDefault(); (e.shiftKey ? last : first).focus(); }
			else if (e.shiftKey && active === first) { e.preventDefault(); last.focus(); }
			else if (!e.shiftKey && active === last) { e.preventDefault(); first.focus(); }
		};
		window.addEventListener('keydown', onKey);
		return () => window.removeEventListener('keydown', onKey);
	}, [onClose]);

	const html = useMemo(() => (view.phase === 'text' && kind === 'markdown' && !source ? renderFileMarkdown(view.text) : ''), [view, kind, source]);

	return (
		<div className="backdrop sheet-backdrop" onClick={onClose}>
			<div ref={sheet} className="sheet pv-sheet" role="dialog" aria-modal="true" aria-labelledby={titleId} data-testid="preview-sheet" onClick={(e) => e.stopPropagation()}>
				<div className="sheet-grab" />
				<div className="pv-head">
					<div className="pv-titles">
						<h2 id={titleId}>{target.title ?? target.name}</h2>
						<div className="pv-meta">{target.title && target.title !== target.name ? `${target.name} · ` : ''}{formatBytes(target.size)}</div>
					</div>
					{kind === 'markdown' && view.phase === 'text' && (
						<button type="button" className="btn small" aria-pressed={source} onClick={() => setSource((v) => !v)}>{source ? 'Rendered' : 'Source'}</button>
					)}
					<button ref={closeBtn} type="button" className="btn small" onClick={onClose} aria-label="Close preview">Close</button>
				</div>
				<div className="pv-body" data-testid="preview-body" aria-live="polite">
					{view.phase === 'loading' && (
						<div className="pv-progress" role="progressbar" aria-label="Loading file" aria-valuemin={0} aria-valuemax={100}
							aria-valuenow={view.total > 0 ? Math.min(100, Math.round((view.loaded / view.total) * 100)) : undefined}>
							<div className="bar"><i style={{ width: `${view.total > 0 ? Math.min(100, (view.loaded / view.total) * 100) : 5}%` }} /></div>
							<span>Loading… {formatBytes(view.loaded)}{view.total > 0 ? ` of ${formatBytes(view.total)}` : ''}</span>
						</div>
					)}
					{view.phase === 'tooLarge' && (
						<p className="pv-note" role="status" data-testid="preview-too-large">
							Too large to preview on this device: {formatBytes(target.size)} (limit {formatBytes(view.limit)} for this type). Open it on the desktop.
						</p>
					)}
					{view.phase === 'unsupported' && <p className="pv-note" role="status">This type of file cannot be previewed here.</p>}
					{view.phase === 'error' && <p className="pv-note error" role="alert">{view.message}</p>}
					{view.phase === 'idle' && (
						<div className="pv-pdf">
							<p className="pv-note">PDF · {formatBytes(target.size)}. Load it to open it in a new tab or download it.</p>
							<button type="button" className="btn primary" onClick={() => setWantPdf(true)}>Load PDF</button>
						</div>
					)}
					{view.phase === 'text' && kind === 'markdown' && !source && <div className="md pv-md" data-testid="preview-md" dangerouslySetInnerHTML={{ __html: html }} />}
					{view.phase === 'text' && (kind !== 'markdown' || source) && <pre className="pv-text" data-testid="preview-text" tabIndex={0}>{view.text}</pre>}
					{view.phase === 'blob' && kind === 'image' && <ImageView url={view.url} blob={view.blob} name={target.name} />}
					{view.phase === 'blob' && kind === 'video' && <VideoView url={view.url} />}
					{view.phase === 'blob' && kind === 'pdf' && (
						<div className="pv-pdf">
							<p className="pv-note">PDF ready · {formatBytes(view.blob.size)}</p>
							<div className="pv-actions">
								<a className="btn primary" href={view.url} target="_blank" rel="noopener noreferrer">Open in new tab</a>
								<a className="btn" href={view.url} download={target.name}>Download</a>
							</div>
						</div>
					)}
					{view.phase === 'blob' && kind !== 'pdf' && (
						<div className="pv-actions"><a className="btn small" href={view.url} download={target.name}>Download</a></div>
					)}
				</div>
			</div>
		</div>
	);
}
