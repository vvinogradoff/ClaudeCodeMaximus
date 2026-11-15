import { deflateSync } from 'node:zlib';

const TABLE = (() => {
	const t = new Uint32Array(256);
	for (let n = 0; n < 256; n++) { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; t[n] = c >>> 0; }
	return t;
})();
const crc32 = (b: Buffer) => { let c = 0xffffffff; for (const x of b) c = TABLE[(c ^ x) & 0xff] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; };

function chunk(type: string, data: Buffer): Buffer {
	const len = Buffer.alloc(4); len.writeUInt32BE(data.length);
	const td = Buffer.concat([Buffer.from(type, 'ascii'), data]);
	const crc = Buffer.alloc(4); crc.writeUInt32BE(crc32(td));
	return Buffer.concat([len, td, crc]);
}

/** A w x h RGB gradient PNG (test image for the https media stubs). */
export function gradientPng(w: number, h: number): Buffer {
	const raw = Buffer.alloc((w * 3 + 1) * h);
	for (let y = 0; y < h; y++) {
		const row = y * (w * 3 + 1);
		raw[row] = 0;
		for (let x = 0; x < w; x++) {
			raw[row + 1 + x * 3] = Math.round((x / w) * 255);
			raw[row + 2 + x * 3] = Math.round((y / h) * 200);
			raw[row + 3 + x * 3] = 180;
		}
	}
	const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(w, 0); ihdr.writeUInt32BE(h, 4); ihdr[8] = 8; ihdr[9] = 2;
	return Buffer.concat([Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]), chunk('IHDR', ihdr), chunk('IDAT', deflateSync(raw)), chunk('IEND', Buffer.alloc(0))]);
}
