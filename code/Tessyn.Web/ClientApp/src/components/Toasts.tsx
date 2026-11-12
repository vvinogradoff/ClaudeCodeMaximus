import { useApp } from '../state/AppContext';

export function Toasts() {
	const { state, actions } = useApp();
	if (state.toasts.length === 0) return null;
	return (
		<div className="toasts" aria-live="polite">
			{state.toasts.map((t) => (
				<div key={t.id} className={`toast ${t.kind}`} onClick={() => actions.dispatch({ type: 'toastDismiss', id: t.id })}>{t.text}</div>
			))}
		</div>
	);
}
