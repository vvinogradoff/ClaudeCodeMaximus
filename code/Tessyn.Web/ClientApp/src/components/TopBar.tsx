import { useEffect, useRef, useState } from 'react';
import { useApp } from '../state/AppContext';

export function TopBar({ onSettings, onCompact }: { onSettings: () => void; onCompact: () => void }) {
	const { state, actions } = useApp();
	const [menu, setMenu] = useState(false);
	const ref = useRef<HTMLDivElement>(null);
	const open = state.session;

	useEffect(() => {
		if (!menu) return;
		const close = (e: Event) => { if (!ref.current?.contains(e.target as Node)) setMenu(false); };
		document.addEventListener('pointerdown', close);
		return () => document.removeEventListener('pointerdown', close);
	}, [menu]);

	return (
		<header className="topbar">
			<button type="button" className="icon-btn" aria-label="Sessions" aria-pressed={state.panelOpen} onClick={() => actions.setPanel(!state.panelOpen)}>☰</button>
			<button type="button" className="title-btn" onClick={() => actions.setPanel(true)}>
				{open?.detail?.name ?? (open ? 'Loading…' : 'Tessyn')}
			</button>
			<span className={`link-dot ${state.online ? 'on' : 'off'}`} title={state.online ? 'Desktop online' : 'Desktop offline'} aria-label={state.online ? 'online' : 'offline'} />
			{open?.detail && <button type="button" className="icon-btn" aria-label="Session settings" onClick={onSettings}>⚙</button>}
			<div className="menu-wrap" ref={ref}>
				<button type="button" className="icon-btn" aria-label="Menu" onClick={() => setMenu((m) => !m)}>⋯</button>
				{menu && (
					<div className="menu" role="menu">
						{open?.detail && <button type="button" role="menuitem" onClick={() => { setMenu(false); onCompact(); }}>Compact session…</button>}
						<button type="button" role="menuitem" onClick={() => { setMenu(false); void actions.logout(); }}>Log out</button>
					</div>
				)}
			</div>
		</header>
	);
}
