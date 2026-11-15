// Attaching files to a prompt: size check and inserting "Attached file: <path>" at the caret.
import { formatBytes, MAX_ATTACHMENT_BYTES } from './media';

/** Error text when the file may not be uploaded, else null. */
export function attachmentProblem(file: { name: string; size: number }): string | null {
	if (file.size <= 0) return `"${file.name}" is empty.`;
	if (file.size > MAX_ATTACHMENT_BYTES) return `"${file.name}" is ${formatBytes(file.size)}; the limit is ${formatBytes(MAX_ATTACHMENT_BYTES)}.`;
	return null;
}

export function attachedLine(path: string): string {
	return `Attached file: ${path}`;
}

/**
 * Inserts the "Attached file: <path>" line at the caret (selection replaced). It starts on its own line unless the caret is at the
 * start of a line, and ends with a line break so the user continues typing below it. Returns the new text and caret position.
 */
export function insertAttachedLine(text: string, caretStart: number, caretEnd: number, path: string): { text: string; caret: number } {
	const start = Math.max(0, Math.min(caretStart, text.length));
	const end = Math.max(start, Math.min(caretEnd, text.length));
	const before = text.slice(0, start);
	const after = text.slice(end);
	const lead = before === '' || before.endsWith('\n') ? '' : '\n';
	const insert = `${lead}${attachedLine(path)}\n`;
	return { text: before + insert + after, caret: before.length + insert.length };
}
