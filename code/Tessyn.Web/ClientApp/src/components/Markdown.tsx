import { memo, useMemo } from 'react';
import type { MouseEvent } from 'react';
import { renderMarkdown } from '../lib/markdown';

async function copyText(text: string): Promise<void> {
	try {
		await navigator.clipboard.writeText(text);
	} catch {
		const ta = document.createElement('textarea');
		ta.value = text;
		ta.style.position = 'fixed';
		ta.style.opacity = '0';
		document.body.appendChild(ta);
		ta.select();
		document.execCommand('copy');
		ta.remove();
	}
}

export const Markdown = memo(function Markdown({ text, className }: { text: string; className?: string }) {
	const html = useMemo(() => renderMarkdown(text), [text]);
	const onClick = (e: MouseEvent<HTMLDivElement>) => {
		const btn = (e.target as HTMLElement).closest('button[data-copy]');
		if (!btn) return;
		const code = btn.parentElement?.querySelector('pre')?.textContent ?? '';
		void copyText(code).then(() => {
			btn.textContent = 'Copied';
			window.setTimeout(() => { btn.textContent = 'Copy'; }, 1200);
		});
	};
	return <div className={`md ${className ?? ''}`} onClick={onClick} dangerouslySetInnerHTML={{ __html: html }} />;
});
