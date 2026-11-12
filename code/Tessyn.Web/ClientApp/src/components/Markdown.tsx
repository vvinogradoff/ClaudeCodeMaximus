import { memo, useLayoutEffect, useMemo, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
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

/** Added by React (never part of the sanitised HTML) into every code block wrapper. */
function CopyButton({ block }: { block: HTMLElement }) {
	const [copied, setCopied] = useState(false);
	const onClick = () => {
		void copyText(block.querySelector('pre')?.textContent ?? '').then(() => {
			setCopied(true);
			window.setTimeout(() => setCopied(false), 1200);
		});
	};
	return <button type="button" className="copy" onClick={onClick}>{copied ? 'Copied' : 'Copy'}</button>;
}

export const Markdown = memo(function Markdown({ text, className }: { text: string; className?: string }) {
	const html = useMemo(() => renderMarkdown(text), [text]);
	const host = useRef<HTMLDivElement>(null);
	const [blocks, setBlocks] = useState<HTMLElement[]>([]);
	useLayoutEffect(() => {
		setBlocks(Array.from(host.current?.querySelectorAll<HTMLElement>('div.code') ?? []));
	}, [html]);
	return (
		<>
			<div ref={host} className={`md ${className ?? ''}`} dangerouslySetInnerHTML={{ __html: html }} />
			{blocks.map((b, i) => createPortal(<CopyButton block={b} />, b, `${i}`))}
		</>
	);
});
