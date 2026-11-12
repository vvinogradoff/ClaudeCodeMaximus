import { useState } from 'react';
import type { FormEvent } from 'react';
import { LoginError } from '../api/rpc';
import { useApp } from '../state/AppContext';

export function Login() {
	const { actions } = useApp();
	const [email, setEmail] = useState('');
	const [password, setPassword] = useState('');
	const [error, setError] = useState<string | null>(null);
	const [busy, setBusy] = useState(false);

	const submit = async (e: FormEvent) => {
		e.preventDefault();
		setBusy(true);
		setError(null);
		try {
			await actions.login(email.trim(), password);
		} catch (err) {
			if (err instanceof LoginError && err.status === 429) {
				const s = err.retryAfterSeconds;
				setError(s ? `Too many attempts. Try again in ${s >= 120 ? Math.ceil(s / 60) + ' minutes' : s + ' seconds'}.` : 'Too many attempts. Try again later.');
			} else {
				setError(err instanceof Error ? err.message : 'Login failed');
			}
		} finally {
			setBusy(false);
		}
	};

	return (
		<div className="login">
			<form className="login-card" onSubmit={(e) => void submit(e)}>
				<h1>Tessyn</h1>
				<label>Email
					<input type="email" name="email" autoComplete="username" autoCapitalize="none" value={email} onChange={(e) => setEmail(e.target.value)} required />
				</label>
				<label>Password
					<input type="password" name="password" autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)} required />
				</label>
				{error && <div className="form-error" role="alert">{error}</div>}
				<button type="submit" className="btn primary" disabled={busy || !email || !password}>{busy ? 'Signing in…' : 'Sign in'}</button>
			</form>
		</div>
	);
}
