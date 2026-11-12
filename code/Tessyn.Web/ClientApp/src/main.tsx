import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { httpTransport } from './api/http';
import { setTransport } from './api/rpc';
import { App } from './App';
import './styles.css';

async function boot(): Promise<void> {
	// VITE_MOCK is a build-time constant: in the production build this branch (and the mock module) is removed.
	if (import.meta.env.VITE_MOCK === '1') {
		const { installMock } = await import('./mock/install');
		installMock();
	} else {
		setTransport(httpTransport);
		if ('serviceWorker' in navigator && import.meta.env.PROD) {
			window.addEventListener('load', () => { void navigator.serviceWorker.register('/sw.js').catch(() => {}); });
		}
	}
	createRoot(document.getElementById('root')!).render(<StrictMode><App /></StrictMode>);
}

void boot();
