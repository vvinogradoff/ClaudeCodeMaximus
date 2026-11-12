import { useEffect, useRef, useState } from 'react';
import type { FormEvent, ReactNode } from 'react';

export function Dialog({ title, children, onClose }: { title: string; children: ReactNode; onClose: () => void }) {
	useEffect(() => {
		const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
		window.addEventListener('keydown', onKey);
		return () => window.removeEventListener('keydown', onKey);
	}, [onClose]);
	return (
		<div className="backdrop" onClick={onClose}>
			<div className="dialog" role="dialog" aria-label={title} onClick={(e) => e.stopPropagation()}>
				<h2>{title}</h2>
				{children}
			</div>
		</div>
	);
}

export function ConfirmDialog(props: { title: string; message: string; confirmLabel: string; onConfirm: () => void; onCancel: () => void }) {
	return (
		<Dialog title={props.title} onClose={props.onCancel}>
			<p>{props.message}</p>
			<div className="dialog-actions">
				<button type="button" className="btn" onClick={props.onCancel}>Cancel</button>
				<button type="button" className="btn primary" onClick={props.onConfirm}>{props.confirmLabel}</button>
			</div>
		</Dialog>
	);
}

export function NameDialog(props: { title: string; label: string; confirmLabel: string; onSubmit: (name: string) => void; onCancel: () => void }) {
	const [name, setName] = useState('');
	const ref = useRef<HTMLInputElement>(null);
	useEffect(() => { ref.current?.focus(); }, []);
	const submit = (e: FormEvent) => {
		e.preventDefault();
		if (name.trim()) props.onSubmit(name.trim());
	};
	return (
		<Dialog title={props.title} onClose={props.onCancel}>
			<form onSubmit={submit}>
				<label className="field">{props.label}
					<input ref={ref} value={name} onChange={(e) => setName(e.target.value)} placeholder="Name" />
				</label>
				<div className="dialog-actions">
					<button type="button" className="btn" onClick={props.onCancel}>Cancel</button>
					<button type="submit" className="btn primary" disabled={!name.trim()}>{props.confirmLabel}</button>
				</div>
			</form>
		</Dialog>
	);
}
