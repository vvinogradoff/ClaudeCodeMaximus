import { useEffect, useState } from 'react';
import { Login } from './components/Login';
import { Main } from './components/Main';
import { AppProvider, useApp } from './state/AppContext';

function Gate() {
	const { state } = useApp();
	if (state.auth === 'unknown') return <div className="splash">Tessyn</div>;
	if (state.auth === 'out') return <Login />;
	return <Main />;
}

/** Re-renders every `intervalMs` so recency shading and relative times stay current. */
export function useTick(intervalMs: number): number {
	const [n, setN] = useState(0);
	useEffect(() => {
		const t = window.setInterval(() => setN((x) => x + 1), intervalMs);
		return () => window.clearInterval(t);
	}, [intervalMs]);
	return n;
}

export function App() {
	return <AppProvider><Gate /></AppProvider>;
}
