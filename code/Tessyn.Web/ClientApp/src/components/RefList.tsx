import type { MouseEvent } from 'react';
import type { AttentionRefDto } from '../api/types';
import { formatBytes } from '../lib/media';
import { safeHttpUrl } from '../lib/url';
import { useApp } from '../state/AppContext';

function hostOf(url: string): string {
	try { return new URL(url).hostname; } catch { return url; }
}

/** The references of a Focus card: jump to an entry of the output, preview a file, open a link. */
export function RefList({ refs }: { refs: AttentionRefDto[] }) {
	const { actions } = useApp();
	const stop = (e: MouseEvent) => e.stopPropagation(); // closed cards toggle on click
	return (
		<ul className="refs" aria-label="References" data-testid="refs" onClick={stop}>
			{refs.map((r, i) => {
				if (r.kind === 'entry') {
					const label = r.label?.trim() || 'Output excerpt';
					return (
						<li key={`${i}-${r.entryId}`}>
							<button type="button" className="ref-chip entry" data-testid="ref-entry" aria-label={`Jump to output: ${label}`} onClick={() => void actions.jumpToEntry(r)}>
								<span className="ref-ico" aria-hidden="true">↪</span><span className="ref-label">{label}</span>
							</button>
						</li>
					);
				}
				if (r.kind === 'file') {
					const label = r.label?.trim() || r.name;
					return (
						<li key={`${i}-${r.refId}`}>
							<button type="button" className="ref-chip file" data-testid="ref-file" aria-label={`Preview file: ${label}`} title={r.path ?? r.name} onClick={() => actions.openFileRef(r)}>
								<span className="ref-ico" aria-hidden="true">📄</span><span className="ref-label">{label}</span>
								<span className="ref-meta">{formatBytes(r.size)}</span>
							</button>
						</li>
					);
				}
				const href = safeHttpUrl(r.url);
				const label = r.label?.trim() || hostOf(r.url);
				return (
					<li key={`${i}-${r.url}`}>
						{href
							? <a className="ref-chip url" data-testid="ref-url" href={href} target="_blank" rel="noopener noreferrer" aria-label={`Open link in a new tab: ${label}`}><span className="ref-ico" aria-hidden="true">🔗</span><span className="ref-label">{label}</span></a>
							: <span className="ref-chip url disabled" title="Unsupported link"><span className="ref-ico" aria-hidden="true">🔗</span><span className="ref-label">{label}</span></span>}
					</li>
				);
			})}
		</ul>
	);
}
