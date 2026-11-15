// Remembers the control that was last activated, so a sheet can return focus to it on close.
// document.activeElement is not enough: Safari does not focus a button when it is clicked.

let last: HTMLElement | null = null;
let installed = false;

export function trackActivations(): void {
	if (installed || typeof document === 'undefined') return;
	installed = true;
	document.addEventListener('click', (e) => {
		const t = e.target instanceof Element ? e.target.closest<HTMLElement>('button, a[href], [role="button"]') : null;
		last = t;
	}, true);
}

/** The element to give focus back to: the focused one, else the last activated control. */
export function currentOpener(): HTMLElement | null {
	const a = document.activeElement;
	if (a instanceof HTMLElement && a !== document.body) return a;
	return last && last.isConnected ? last : null;
}
