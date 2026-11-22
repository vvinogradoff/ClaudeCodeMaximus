import { describe, expect, it } from 'vitest';
import type { CatalogResult, HelloResult, UsageResult } from '../api/types';
import { initialState, reduce } from '../state/appState';
import {
	codexStatusLines, effectiveChoice, effortOptions, GROUP_CLAUDE, GROUP_GPT, GROUP_LOCAL, hasStreaming, modelEngine, modelGroups, modelTitle, profilesFor, usageBars,
} from './engines';

const CLAUDE_EFFORTS = [{ id: 'max' }, { id: 'high' }, { id: 'medium' }, { id: 'low' }];

/** The shape of catalog.get with GPT models (TC.CX.39 golden `catalog.get.gpt`). */
const catalog: CatalogResult = {
	models: [
		{ id: 'default', displayName: 'Default', engine: 'claude', efforts: CLAUDE_EFFORTS },
		{ id: 'claude-opus-5-5', displayName: 'Opus 5.5', provider: 'anthropic', engine: 'claude', efforts: CLAUDE_EFFORTS },
		{ id: 'gpt-x-astra', displayName: 'GPT-X-Astra', provider: 'openAI', engine: 'codex', efforts: [{ id: 'low' }, { id: 'medium' }] },
		{ id: 'gpt-x-sol', displayName: 'GPT-X-Sol', provider: 'openAI', engine: 'codex', isDefault: true, upgrade: 'gpt-x-sol-2',
			efforts: [{ id: 'low', description: 'low reasoning' }, { id: 'xhigh', description: 'xhigh reasoning' }, { id: 'ultra', description: 'sub-agents' }] },
		{ id: 'qwen3:8b', displayName: 'qwen3:8b', provider: 'ollama', engine: 'claude', efforts: CLAUDE_EFFORTS },
	],
	efforts: ['default', 'max', 'high', 'medium', 'low'],
	profiles: [
		{ id: 'default', displayName: '[Sys] a@example.com', engine: 'claude' },
		{ id: 'profile_1', displayName: 'Work', engine: 'claude' },
		{ id: 'codex_1', displayName: 'ChatGPT · u@example.com', engine: 'codex', plan: 'plus' },
		{ id: 'codex_2', displayName: 'ChatGPT · w@example.com', engine: 'codex', plan: 'pro' },
	],
};

/** An older daemon: no engine, no per-model efforts. */
const oldCatalog: CatalogResult = {
	models: [{ id: 'default', displayName: 'Default' }, { id: 'claude-opus-5-5', displayName: 'Opus 5.5', provider: 'anthropic' }],
	efforts: ['default', 'max', 'high', 'medium', 'low'],
	profiles: [{ id: 'default', displayName: 'Default' }, { id: 'work', displayName: 'Work' }],
};

describe('model picker (FR.33.2)', () => {
	it('puts Default first, then Claude, GPT (ChatGPT plan) with the default GPT model first, and Local', () => {
		const { defaults, groups } = modelGroups(catalog);
		expect(defaults.map((m) => m.id)).toEqual(['default']);
		expect(groups.map((g) => g.label)).toEqual([GROUP_CLAUDE, GROUP_GPT, GROUP_LOCAL]);
		expect(groups[0].models.map((m) => m.id)).toEqual(['claude-opus-5-5']);
		expect(groups[1].models.map((m) => m.id)).toEqual(['gpt-x-sol', 'gpt-x-astra']);
		expect(groups[2].models.map((m) => m.id)).toEqual(['qwen3:8b']);
	});

	it('shows the hint line in the GPT group when there are no GPT models', () => {
		const hinted: CatalogResult = { ...catalog, models: catalog.models.filter((m) => m.engine !== 'codex'), gptHint: 'GPT models: sign in to ChatGPT in Settings > Profiles' };
		const gpt = modelGroups(hinted).groups.find((g) => g.label === GROUP_GPT)!;
		expect(gpt.models).toEqual([]);
		expect(gpt.hint).toBe('GPT models: sign in to ChatGPT in Settings > Profiles');
	});

	it('has no GPT group without GPT models and without a hint (Codex not found, or an older daemon)', () => {
		expect(modelGroups(oldCatalog).groups.map((g) => g.label)).toEqual([GROUP_CLAUDE]);
		expect(modelGroups(null).defaults.map((m) => m.id)).toEqual(['default']);
	});

	it('gives a GPT model Codex\'s upgrade hint as its tooltip', () => {
		expect(modelTitle(catalog.models[3])).toBe('Codex suggests gpt-x-sol-2');
		expect(modelTitle(catalog.models[1])).toBeUndefined();
	});
});

describe('effort picker (FR.33.3)', () => {
	it('offers Default and the GPT model\'s own efforts with their descriptions, without Ultra', () => {
		expect(effortOptions(catalog, 'gpt-x-sol')).toEqual([
			{ id: 'default', label: 'Default' },
			{ id: 'low', label: 'Low', description: 'low reasoning' },
			{ id: 'xhigh', label: 'Xhigh', description: 'xhigh reasoning' },
		]);
	});

	it('offers the Claude list for a Claude model, and the global list for an older daemon', () => {
		expect(effortOptions(catalog, 'claude-opus-5-5').map((e) => e.id)).toEqual(['default', 'max', 'high', 'medium', 'low']);
		expect(effortOptions(oldCatalog, 'claude-opus-5-5').map((e) => e.id)).toEqual(['default', 'max', 'high', 'medium', 'low']);
	});
});

describe('profile picker (FR.33.5)', () => {
	it('lists only the profiles of the model\'s engine', () => {
		expect(profilesFor(catalog, 'codex').map((p) => p.id)).toEqual(['codex_1', 'codex_2']);
		expect(profilesFor(catalog, 'claude').map((p) => p.id)).toEqual(['default', 'profile_1']);
		expect(profilesFor(oldCatalog, 'claude').map((p) => p.id)).toEqual(['default', 'work']);
	});
});

describe('the choice a send carries (FR.25.24)', () => {
	it('keeps a GPT session\'s own choice', () => {
		expect(effectiveChoice(catalog, { modelId: 'gpt-x-sol', effort: 'xhigh', profileId: 'codex_2' })).toEqual(
			{ engine: 'codex', modelId: 'gpt-x-sol', effort: 'xhigh', profileId: 'codex_2' });
	});

	it('moves to a ChatGPT profile and Default effort when a Claude session picks a GPT model whose efforts lack the stored one', () => {
		expect(effectiveChoice(catalog, { modelId: 'gpt-x-astra', effort: 'max', profileId: 'profile_1' })).toEqual(
			{ engine: 'codex', modelId: 'gpt-x-astra', effort: 'default', profileId: 'codex_1' });
	});

	it('moves back to a Claude profile and shows a GPT-only effort as Default for a Claude model', () => {
		expect(effectiveChoice(catalog, { modelId: 'claude-opus-5-5', effort: 'xhigh', profileId: 'codex_1' })).toEqual(
			{ engine: 'claude', modelId: 'claude-opus-5-5', effort: 'default', profileId: 'default' });
	});

	it('sends no profile for a GPT model when there is no ChatGPT profile', () => {
		const noChatGpt: CatalogResult = { ...catalog, profiles: catalog.profiles.filter((p) => p.engine !== 'codex') };
		expect(effectiveChoice(noChatGpt, { modelId: 'gpt-x-sol', effort: 'low', profileId: 'default' }).profileId).toBeUndefined();
	});

	it('keeps an unlisted profile for a Claude model and everything before the catalog loads', () => {
		expect(effectiveChoice(catalog, { modelId: 'default', effort: 'high', profileId: 'gone' }).profileId).toBe('gone');
		expect(effectiveChoice(null, { modelId: 'gpt-x-sol', effort: 'xhigh', profileId: 'codex_1' }, 'codex')).toEqual(
			{ engine: 'codex', modelId: 'gpt-x-sol', effort: 'xhigh', profileId: 'codex_1' });
	});

	it('takes the session\'s engine for a model the catalog does not list (FR.33.4), else Claude', () => {
		expect(modelEngine(catalog, 'gpt-retired', 'codex')).toBe('codex');
		expect(modelEngine(catalog, 'gpt-retired')).toBe('claude');
		expect(modelEngine(oldCatalog, 'claude-opus-5-5')).toBe('claude');
	});

	it('hides Streaming for a GPT model (FR.33.1)', () => {
		expect(hasStreaming('codex')).toBe(false);
		expect(hasStreaming('claude')).toBe(true);
	});
});

describe('usage strip (FR.33.10)', () => {
	const window = (extra: object = {}) => ({ utilization: 40, resetsAtUtc: '2026-11-22T14:00:00Z', isStale: false, ...extra });

	it('labels the bars from the windows and marks the exhausted one with Codex\'s reason', () => {
		const usage: UsageResult = {
			profileId: 'codex_1', profileName: 'ChatGPT · u@example.com', state: 'ok', engine: 'codex', exhausted: 'rate_limit_reached',
			fiveHour: window({ label: '3h', durationMinutes: 180, isExhausted: true, utilization: 100 }), sevenDay: window({ label: '7d', durationMinutes: 10080 }),
		};
		const bars = usageBars(usage);
		expect(bars.map((b) => [b.name, b.exhausted])).toEqual([['3h', true], ['7d', false]]);
		expect(bars[0].title).toBe('3h: 100%, out of credits (rate_limit_reached)');
		expect(bars[1].title).toBe('7d: 40%');
	});

	it('falls back to 5h / 7d for an older daemon and hides an absent window', () => {
		expect(usageBars({ profileId: 'default', profileName: 'Default', state: 'ok', fiveHour: window(), sevenDay: window() }).map((b) => b.name)).toEqual(['5h', '7d']);
		expect(usageBars({ profileId: 'codex_1', profileName: 'P', state: 'ok', engine: 'codex', fiveHour: window({ label: '7d' }) }).map((b) => b.name)).toEqual(['7d']);
	});
});

describe('read-only Codex status (FR.33.17)', () => {
	it('says found with the version and the note, or not found', () => {
		expect(codexStatusLines({ found: true, version: '0.172.0', testedVersion: '0.171', note: 'newer than tested' })).toEqual({ status: 'Codex 0.172.0 found', note: 'newer than tested' });
		expect(codexStatusLines({ found: true, testedVersion: '0.171' })).toEqual({ status: 'Codex found (version unknown)', note: undefined });
		expect(codexStatusLines({ found: false, testedVersion: '0.171' })).toEqual({ status: 'Codex not found on the desktop' });
		expect(codexStatusLines(null)).toBeNull();
	});

	it('is kept from client.hello, and an older daemon without it clears it', () => {
		const hello: HelloResult = { protocolVersion: 1, daemonVersion: '1', machineName: 'PC', runId: 'r', serverTimeUtc: new Date().toISOString(), codex: { found: true, version: '0.171.0', testedVersion: '0.171' } };
		const withCodex = reduce(initialState('tree'), { type: 'hello', hello });
		expect(withCodex.codex).toEqual({ found: true, version: '0.171.0', testedVersion: '0.171' });
		const { codex: _omitted, ...older } = hello;
		expect(reduce(withCodex, { type: 'hello', hello: older }).codex).toBeNull();
	});
});
