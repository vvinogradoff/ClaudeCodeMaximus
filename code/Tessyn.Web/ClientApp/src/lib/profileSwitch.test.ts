import { describe, expect, it } from 'vitest';
import type { SwitchProfileResult } from '../api/types';
import { canSwitchTo, currentProfileId, rowProfileLabel, summarizeSwitch } from './profileSwitch';

const detail = (pinned: string | undefined, lastUsed = 'default') => ({
	state: { id: 'n', running: false, streamingAlive: false, favorite: false, hasDraft: false, pinnedProfileId: pinned },
	lastUsed: { modelId: 'default', effort: 'default', profileId: lastUsed, profileName: lastUsed },
});

const result = (patch: Partial<SwitchProfileResult> = {}): SwitchProfileResult =>
	({ profileId: 'work', profileName: 'Work', nodeMoved: true, moved: [], queued: [], failed: [], ...patch });

describe('profile switch (ISSUE.013)', () => {
	it('takes the pin over the last used profile as the current one', () => {
		expect(currentProfileId(detail('work', 'default'))).toBe('work');
		expect(currentProfileId(detail(undefined, 'personal'))).toBe('personal');
	});

	it('offers the switch only for another profile than the current one', () => {
		expect(canSwitchTo(detail('work'), 'work')).toBe(false);
		expect(canSwitchTo(detail('work'), 'personal')).toBe(true);
		expect(canSwitchTo(detail(undefined, 'default'), 'default')).toBe(false);
		expect(canSwitchTo(detail('work'), undefined)).toBe(false);
	});

	describe('row label', () => {
		it('keeps the last prompt profile when there is no pin or it names the same profile', () => {
			expect(rowProfileLabel({ profileName: 'Work (a@b)' })).toBe('Work (a@b)');
			expect(rowProfileLabel({ profileName: 'Work', pinnedProfileId: 'work', pinnedProfileName: 'Work' })).toBe('Work');
		});

		it('keeps the [Sys] label of a session pinned to Default', () => {
			expect(rowProfileLabel({ profileName: '[Sys] a@b.c', pinnedProfileId: 'default', pinnedProfileName: 'default' })).toBe('[Sys] a@b.c');
		});

		it('shows the pin once a switch moved it away from the last prompt profile', () => {
			expect(rowProfileLabel({ profileName: '[Sys] a@b.c', pinnedProfileId: 'work', pinnedProfileName: 'Work' })).toBe('Work');
			expect(rowProfileLabel({ profileName: 'Work', pinnedProfileId: 'default', pinnedProfileName: 'default' })).toBe('default');
		});

		it('appends a queued switch', () => {
			expect(rowProfileLabel({ profileName: 'Work', switchingToProfileName: 'Personal' })).toBe('Work → Personal');
			expect(rowProfileLabel({ switchingToProfileName: 'Personal' })).toBe('→ Personal');
		});
	});

	describe('summary', () => {
		it('names the session and counts the workers', () => {
			expect(summarizeSwitch(result({ moved: [{ nodeId: 'a', name: 'A', fromProfileId: 'x', fromProfileName: 'x' }] }), 'Boss'))
				.toEqual({ text: '"Boss" moved to Work; 1 worker moved', problem: false });
		});

		it('reports running sessions as switched when the turn ends', () => {
			const queued = { nodeId: 'a', name: 'A', fromProfileId: 'x', fromProfileName: 'x' };
			expect(summarizeSwitch(result({ nodeMoved: false, queued: [queued, queued] }), 'Boss').text).toBe('2 running, switched when their turn ends');
		});

		it('flags failures with names and file counts, never paths', () => {
			const s = summarizeSwitch(result({ failed: [{ nodeId: 'a', name: 'A', fileCount: 2 }] }), 'Boss');
			expect(s.problem).toBe(true);
			expect(s.text).toContain('A');
			expect(s.text).toContain('2 files');
		});

		it('says so when nothing changed', () => {
			expect(summarizeSwitch(result({ nodeMoved: false }), 'Boss')).toEqual({ text: '"Boss" is already on Work', problem: false });
		});
	});
});
