// Reads a whole file through file.read chunks (the relay caps one chunk, so a file takes several calls).
import type { FileReadResult } from '../api/types';

export type ReadChunk = (params: { handle: string; offset: number }) => Promise<FileReadResult>;

export interface ReadControl {
	cancelled(): boolean;
	onProgress(loaded: number, total: number): void;
}

export class ReadCancelled extends Error {
	constructor() { super('cancelled'); this.name = 'ReadCancelled'; }
}

export function base64ToBytes(b64: string): Uint8Array<ArrayBuffer> {
	const bin = atob(b64);
	const out = new Uint8Array(bin.length);
	for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
	return out;
}

/** Text chunk -> string (the daemon sends `utf8` for text types, but an svg can arrive as base64). */
export function chunkToText(c: FileReadResult): string {
	return c.encoding === 'utf8' ? c.data : new TextDecoder('utf-8').decode(base64ToBytes(c.data));
}

/** All chunks of a text file, concatenated. `maxBytes` guards a file that grew after the size check. */
export async function readText(read: ReadChunk, handle: string, size: number, maxBytes: number, ctl: ReadControl): Promise<string> {
	let offset = 0;
	let text = '';
	for (;;) {
		if (ctl.cancelled()) throw new ReadCancelled();
		const c = await read({ handle, offset });
		if (ctl.cancelled()) throw new ReadCancelled();
		text += chunkToText(c);
		const next = c.offset + c.length;
		ctl.onProgress(next, Math.max(size, c.size));
		if (c.eof) return text;
		if (c.length <= 0 || next <= offset) throw new Error('The daemon returned an empty chunk before the end of the file');
		if (next > maxBytes) throw new Error('The file grew beyond the preview limit while it was being read');
		offset = next;
	}
}

/** All chunks of a binary file as one Blob of the given type. */
export async function readBlob(read: ReadChunk, handle: string, size: number, maxBytes: number, mime: string, ctl: ReadControl): Promise<Blob> {
	let offset = 0;
	const parts: BlobPart[] = [];
	for (;;) {
		if (ctl.cancelled()) throw new ReadCancelled();
		const c = await read({ handle, offset });
		if (ctl.cancelled()) throw new ReadCancelled();
		parts.push(c.encoding === 'base64' ? base64ToBytes(c.data) : new TextEncoder().encode(c.data));
		const next = c.offset + c.length;
		ctl.onProgress(next, Math.max(size, c.size));
		if (c.eof) return new Blob(parts, { type: mime });
		if (c.length <= 0 || next <= offset) throw new Error('The daemon returned an empty chunk before the end of the file');
		if (next > maxBytes) throw new Error('The file grew beyond the preview limit while it was being read');
		offset = next;
	}
}
