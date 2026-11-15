import { useEffect, useMemo, useState } from 'react';
import type { SessionEntryDto } from '../api/types';
import { fileNameOf, findLocalPaths, findMediaLinks, hostAllowed } from '../lib/media';
import type { MediaLink } from '../lib/media';
import { useApp } from '../state/AppContext';

function MediaPreview({ link, auto }: { link: MediaLink; auto: boolean }) {
	const [shown, setShown] = useState(auto);
	const [mode, setMode] = useState<'img' | 'video' | 'failed'>(link.kind === 'video' ? 'video' : 'img');
	useEffect(() => { if (auto) setShown(true); }, [auto]);

	if (!shown) {
		return (
			<button type="button" className="btn small load-preview" data-testid="load-preview" onClick={() => setShown(true)}>
				Load preview <span className="host">{link.host}</span>
			</button>
		);
	}
	if (mode === 'failed') {
		return <a className="media-failed" href={link.url} target="_blank" rel="noopener noreferrer">Preview unavailable. Open {link.host}</a>;
	}
	return (
		<figure className="media" data-testid="media-preview">
			{mode === 'img'
				// Unknown kind (allowlisted host without an extension): try an image first, then a video.
				? <img src={link.url} alt={`Preview from ${link.host}`} loading="lazy" referrerPolicy="no-referrer" onError={() => setMode(link.kind === 'unknown' ? 'video' : 'failed')} />
				: <video src={link.url} controls playsInline preload="metadata" onError={() => setMode('failed')} />}
		</figure>
	);
}

/** Under an assistant entry: inline previews of https images/video, and chips for absolute local file paths. */
export function EntryExtras({ entry }: { entry: SessionEntryDto }) {
	const { state, actions } = useApp();
	const hosts = state.catalog?.mediaPreviewHosts;
	const media = useMemo(() => findMediaLinks(entry.content, hosts), [entry.content, hosts]);
	const paths = useMemo(() => (entry.entryId ? findLocalPaths(entry.content) : []), [entry.content, entry.entryId]);
	if (media.length === 0 && paths.length === 0) return null;
	return (
		<div className="extras">
			{media.map((m) => <MediaPreview key={m.url} link={m} auto={hostAllowed(m.host, hosts)} />)}
			{paths.length > 0 && (
				<ul className="refs path-chips" aria-label="Files mentioned in this message">
					{paths.map((p) => (
						<li key={p}>
							<button type="button" className="ref-chip file" data-testid="path-chip" title={p} aria-label={`Preview file: ${fileNameOf(p)}`}
								onClick={() => void actions.previewLocalPath(entry.entryId!, p)}>
								<span className="ref-ico" aria-hidden="true">📄</span><span className="ref-label">{fileNameOf(p)}</span>
							</button>
						</li>
					))}
				</ul>
			)}
		</div>
	);
}
