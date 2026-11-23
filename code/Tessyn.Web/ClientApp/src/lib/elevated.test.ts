import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import type { AttentionItemDto, ElevatedRunDto, ElevatedRunStatus } from '../api/types';
import { ElevatedRunBody } from '../components/ElevatedRunCard';
import { APPROVE_ON_PC, elevatedRunOf, folderName, formatRunDuration, isFinalStatus, isInProgress, openStatusLine, outcomeLine, requestedBy, runningItems } from './elevated';

// TC.ER.62 (unit part): the Focus tab shows the elevated-run card read-only: reason, the folder NAME, the whole command, "Approve on the PC", no button in any state, the outcome at the end.

const LONG_COMMAND = `powershell -NoProfile -Command "Get-ChildItem 'C:\\Windows' | Where-Object { $_.Name -like '*<b>x</b>*' }" ${'x'.repeat(400)}`;

const run = (status: ElevatedRunStatus = 'pending', extra: Partial<ElevatedRunDto> = {}): ElevatedRunDto => ({
	id: 'er-1', status, requester: { nodeId: 'lead-node-0001', name: 'Lead', shortId: 'lead-nod' }, reason: 'Run the WPF E2E suite as the test user',
	workingDirName: 'Tessyn-wt-elevruns', command: 'cmd /c echo hi & exit 3', timeoutMinutes: 60,
	createdUtc: '2026-11-21T09:00:00Z', expiresUtc: '2026-11-21T09:30:00Z', ...extra,
});

const render = (r: ElevatedRunDto, sessionNodeId = 'lead-node-0001', now = Date.parse('2026-11-21T09:10:00Z')) =>
	renderToStaticMarkup(createElement(ElevatedRunBody, { run: r, sessionNodeId, now }));

const htmlEscape = (text: string) => text.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;').replaceAll("'", '&#x27;');

const ALL: ElevatedRunStatus[] = ['pending', 'launching', 'running', 'completed', 'declined', 'declinedAtPrompt', 'failedToStart', 'timedOut', 'cancelled', 'expired'];

describe('elevated-run card on the web', () => {
	it('shows the facts: reason, folder name only, the whole command, the time limit and the approve note while pending', () => {
		const html = render(run('pending', { command: LONG_COMMAND, workingDir: 'C:\\Projects\\Personal\\Tessyn-wt-elevruns' }));

		expect(html).toContain('Run the WPF E2E suite as the test user');
		expect(html).toContain('Tessyn-wt-elevruns');
		expect(html).not.toContain('C:\\Projects');                       // a path never shows, even if one slipped into the DTO
		expect(html).toContain(htmlEscape(LONG_COMMAND));   // whole and verbatim, as text (escaped, not parsed)
		expect(html).toContain('Time limit: 60 minutes');
		expect(html).toContain(APPROVE_ON_PC);
		expect(html).toContain('Requested by Lead (lead-nod)');
	});

	it.each(ALL)('has no button or input in status %s', (status) => {
		const html = render(run(status, { exitCode: 0, startedUtc: '2026-11-21T09:00:00Z', endedUtc: '2026-11-21T09:03:12Z', failure: 'x' }));

		expect(html).not.toMatch(/<button/i);
		expect(html).not.toMatch(/<input/i);
		expect(html).not.toMatch(/<textarea/i);
		expect(html).not.toMatch(/>(Run as administrator|Decline|Stop|Copy command)</);
	});

	it('shows the note only while pending, the status while it launches or runs, and the outcome when it ended', () => {
		expect(render(run('pending'))).toContain(APPROVE_ON_PC);
		expect(render(run('launching'))).not.toContain(APPROVE_ON_PC);
		expect(render(run('launching'))).toContain('Starting on the PC...');
		expect(render(run('running', { startedUtc: '2026-11-21T09:05:00Z' }))).toContain('Running as administrator on the PC (limit 60 min, 5m 00s)');
		const done = render(run('completed', { exitCode: 3, startedUtc: '2026-11-21T09:00:00Z', endedUtc: '2026-11-21T09:03:12Z' }));
		expect(done).toContain('Completed: exit code 3, 3m 12s');
		expect(done).not.toContain(APPROVE_ON_PC);
	});

	it('names the worker on the supervisor card', () => {
		expect(requestedBy(run('pending'), 'LEAD-NODE-0001')).toBe('Requested by Lead (lead-nod)');
		expect(requestedBy(run('pending', { requester: { nodeId: 'wrk-node', name: 'Wrk', shortId: 'wrk-node' } }), 'lead-node-0001')).toBe('Requested by worker Wrk (wrk-node) of this session');
	});

	it.each<[ElevatedRunStatus, Partial<ElevatedRunDto>, string]>([
		['completed', { exitCode: 0, startedUtc: '2026-11-21T09:00:00Z', endedUtc: '2026-11-21T09:03:12Z' }, 'Completed: exit code 0, 3m 12s'],
		['completed', { exitCode: 2 }, 'Completed: exit code 2'],
		['declined', {}, 'Declined'],
		['declinedAtPrompt', {}, 'Declined at the Windows prompt'],
		['failedToStart', { failure: 'The client did not report the start.' }, 'Failed to start: The client did not report the start.'],
		['timedOut', { timeoutMinutes: 60 }, 'Timed out after 60 min'],
		['cancelled', { cancelledBy: 'user' }, 'Cancelled (stopped by the user)'],
		['cancelled', { cancelledBy: 'agent' }, 'Cancelled (withdrawn by the agent)'],
		['expired', {}, 'Expired'],
	])('writes the outcome line of %s', (status, extra, expected) => {
		expect(outcomeLine(run(status, extra))).toBe(expected);
	});

	it('knows the final statuses, the durations and the folder name', () => {
		expect(ALL.filter(isFinalStatus)).toEqual(['completed', 'declined', 'declinedAtPrompt', 'failedToStart', 'timedOut', 'cancelled', 'expired']);
		expect(formatRunDuration(7_000)).toBe('7s');
		expect(formatRunDuration(192_000)).toBe('3m 12s');
		expect(formatRunDuration(3_725_000)).toBe('1h 02m 05s');
		expect(folderName(run('pending', { workingDirName: undefined, workingDir: 'C:\\a\\b c\\' }))).toBe('b c');
		expect(openStatusLine(run('pending'), 0)).toBe('');
	});

	it('reads the run only from the elevatedRun action', () => {
		const item = (action?: AttentionItemDto['action']): AttentionItemDto => ({
			id: 'A1', number: 1, kind: 'ask', title: 't', options: [], status: 'open', createdUtc: '2026-11-21T09:00:00Z', updatedUtc: '2026-11-21T09:00:00Z', action,
		});

		expect(elevatedRunOf(item({ type: 'elevatedRun', run: run() }))?.id).toBe('er-1');
		expect(elevatedRunOf(item())).toBeUndefined();
		expect(elevatedRunOf(item({ type: 'somethingElse', run: run() }))).toBeUndefined();
	});
});

// FR.23.41, FR.23.42: an approved run (launching, running) is in progress: the Running group, not the open cards, not counted.
describe('in-progress elevated runs on the web', () => {
	const item = (id: number, r: ElevatedRunDto | undefined, extra: Partial<AttentionItemDto> = {}): AttentionItemDto => ({
		id: `A${id}`, number: id, kind: 'ask', title: `t${id}`, options: [], status: 'open', createdUtc: `2026-11-21T09:0${id}:00Z`, updatedUtc: '2026-11-21T09:00:00Z',
		action: r ? { type: 'elevatedRun', run: r } : undefined, ...extra,
	});

	it.each<[ElevatedRunStatus, boolean]>([
		['pending', false], ['launching', true], ['running', true], ['completed', false], ['declined', false], ['declinedAtPrompt', false],
		['failedToStart', false], ['timedOut', false], ['cancelled', false], ['expired', false],
	])('status %s in progress: %s', (status, expected) => {
		expect(isInProgress(item(1, run(status)))).toBe(expected);
	});

	it('is false for a plain ask and for a closed item', () => {
		expect(isInProgress(item(1, undefined))).toBe(false);
		expect(isInProgress(item(2, run('running'), { status: 'resolved' }))).toBe(false);
	});

	it('lists the in-progress items oldest approval first and leaves the rest out', () => {
		const items = [
			item(5, run('running', { startedUtc: '2026-11-21T09:09:00Z' })),
			item(1, undefined),
			item(2, run('launching')),                       // not started yet: falls back to its creation time (09:02)
			item(3, run('pending')),
			item(4, run('running', { startedUtc: '2026-11-21T09:01:00Z' })),
		];

		expect(runningItems(items).map((i) => i.id)).toEqual(['A4', 'A2', 'A5']);
		expect(runningItems([item(1, undefined), item(3, run('pending'))])).toEqual([]);
	});
});
