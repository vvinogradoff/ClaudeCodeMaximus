// FR.33 on Tessyn Web: the pickers follow the engine of the chosen model (FR.33.2, FR.33.3, FR.33.5), and the web always sends its choice (FR.25.24).
import type { CatalogResult, CodexHelloStatusDto, EngineId, ModelOptionDto, ProfileOptionDto, UsageResult, UsageWindowDto } from '../api/types';

export const CLAUDE: EngineId = 'claude';
export const CODEX: EngineId = 'codex';
export const DEFAULT_ID = 'default';

/** The group headers of the model picker (FR.33.2). */
export const GROUP_CLAUDE = 'Claude';
export const GROUP_GPT = 'GPT (ChatGPT plan)';
export const GROUP_LOCAL = 'Local';

/** FR.33.3: Ultra means Codex-native sub-agents, which the session tree cannot see. */
const HIDDEN_EFFORTS = new Set(['ultra']);
const FALLBACK_EFFORTS = ['default', 'max', 'high', 'medium', 'low'];

export interface TurnChoice { modelId: string; effort: string; profileId: string }
export interface ModelGroup { label: string; models: ModelOptionDto[]; hint?: string }
export interface EffortOption { id: string; label: string; description?: string }
/** What the pickers show and a send carries: the stored choice, adjusted to the model's engine. */
export interface EffectiveChoice { engine: EngineId; modelId: string; effort: string; profileId?: string }

const capital = (s: string) => s.charAt(0).toUpperCase() + s.slice(1);

export const engineOfModel = (m: ModelOptionDto): EngineId => m.engine ?? CLAUDE;
export const engineOfProfile = (p: ProfileOptionDto): EngineId => p.engine ?? CLAUDE;

/**
 * The engine of a model id (FR.33.4): the catalog's tag; a model not in the catalog keeps the session's own engine
 * (the daemon's rule while the GPT list is unknown), else Claude.
 */
export function modelEngine(catalog: CatalogResult | null, modelId: string, sessionEngine?: EngineId): EngineId {
	const m = catalog?.models.find((x) => x.id === modelId);
	if (m) return engineOfModel(m);
	return sessionEngine ?? CLAUDE;
}

/**
 * The model picker (FR.33.2): Default first (ungrouped), then Claude, GPT (ChatGPT plan) with Codex's default model first, and Local.
 * An empty GPT group carries the catalog's hint line instead (no ChatGPT profile signed in).
 */
export function modelGroups(catalog: CatalogResult | null): { defaults: ModelOptionDto[]; groups: ModelGroup[] } {
	const models = catalog?.models ?? [{ id: DEFAULT_ID, displayName: 'Default' }];
	const defaults = models.filter((m) => m.id === DEFAULT_ID);
	const rest = models.filter((m) => m.id !== DEFAULT_ID);
	const local = rest.filter((m) => m.provider === 'ollama');
	const gpt = rest.filter((m) => engineOfModel(m) === CODEX);
	const claude = rest.filter((m) => engineOfModel(m) !== CODEX && m.provider !== 'ollama');
	gpt.sort((a, b) => Number(!!b.isDefault) - Number(!!a.isDefault)); // stable: Codex's order otherwise
	const groups: ModelGroup[] = [];
	if (claude.length > 0) groups.push({ label: GROUP_CLAUDE, models: claude });
	if (gpt.length > 0) groups.push({ label: GROUP_GPT, models: gpt });
	else if (catalog?.gptHint) groups.push({ label: GROUP_GPT, models: [], hint: catalog.gptHint });
	if (local.length > 0) groups.push({ label: GROUP_LOCAL, models: local });
	return { defaults, groups };
}

/** The tooltip of a model option: Codex's upgrade hint. */
export const modelTitle = (m: ModelOptionDto): string | undefined => (m.upgrade ? `Codex suggests ${m.upgrade}` : undefined);

/** FR.33.3: Default, then the model's own efforts (Codex's order and descriptions, without Ultra); the global list for a model without its own. */
export function effortOptions(catalog: CatalogResult | null, modelId: string): EffortOption[] {
	const m = catalog?.models.find((x) => x.id === modelId);
	if (m?.efforts) {
		return [
			{ id: DEFAULT_ID, label: 'Default' },
			...m.efforts.filter((e) => e.id !== DEFAULT_ID && !HIDDEN_EFFORTS.has(e.id)).map((e) => ({ id: e.id, label: capital(e.id), description: e.description })),
		];
	}
	return (catalog?.efforts ?? FALLBACK_EFFORTS).map((id) => ({ id, label: capital(id) }));
}

/** FR.33.5: a profile is never used across engines, so the picker lists only the profiles of the model's engine. */
export function profilesFor(catalog: CatalogResult | null, engine: EngineId): ProfileOptionDto[] {
	if (!catalog) return [{ id: DEFAULT_ID, displayName: 'Default' }];
	return catalog.profiles.filter((p) => engineOfProfile(p) === engine);
}

/**
 * The choice as shown and sent (FR.25.24 with FR.33.3, FR.33.5). The stored choice is never rewritten, so it comes back when the model changes back:
 * - an effort the model does not offer is shown and sent as Default;
 * - a profile of the other engine is replaced by the first profile of the model's engine (none: no profile is sent, and the daemon names the problem).
 * A profile the catalog does not list (deleted, or the catalog not loaded yet) is kept for a Claude model, as before FR.33.
 */
export function effectiveChoice(catalog: CatalogResult | null, choice: TurnChoice, sessionEngine?: EngineId): EffectiveChoice {
	const engine = modelEngine(catalog, choice.modelId, sessionEngine);
	if (!catalog) return { engine, ...choice };
	const efforts = effortOptions(catalog, choice.modelId);
	const effort = efforts.some((e) => e.id === choice.effort) ? choice.effort : DEFAULT_ID;
	const listed = catalog.profiles.find((p) => p.id === choice.profileId);
	let profileId: string | undefined = choice.profileId;
	if (listed ? engineOfProfile(listed) !== engine : engine !== CLAUDE) profileId = profilesFor(catalog, engine)[0]?.id;
	return { engine, modelId: choice.modelId, effort, profileId };
}

/** FR.33.1: a Codex session has no Streaming toggle. */
export const hasStreaming = (engine: EngineId): boolean => engine !== CODEX;

/** The read-only Codex line of the Settings sheet (FR.33.17), from `client.hello.codex`; null without one (an older daemon). */
export function codexStatusLines(status: CodexHelloStatusDto | null | undefined): { status: string; note?: string } | null {
	if (!status) return null;
	if (!status.found) return { status: 'Codex not found on the desktop' };
	const text = status.version ? `Codex ${status.version} found` : 'Codex found (version unknown)';
	return { status: text, note: status.note };
}

export interface UsageBar { name: string; window: UsageWindowDto; exhausted: boolean; title: string }

/**
 * The usage strip's bars (FR.25.27, FR.33.10): each label comes from the window (`5h`, `7d`, `<n>h`), with 5h / 7d for an older daemon; a window
 * that is absent is hidden (Pro plans have no short window). The window that ran out says Codex's reason in its tooltip.
 */
export function usageBars(usage: UsageResult): UsageBar[] {
	const bars: UsageBar[] = [];
	const add = (w: UsageWindowDto | undefined, fallback: string) => {
		if (!w) return;
		const name = w.label ?? fallback;
		const exhausted = !!w.isExhausted;
		const pct = Math.max(0, Math.min(100, Math.round(w.utilization)));
		bars.push({ name, window: w, exhausted, title: exhausted ? `${name}: ${pct}%, out of credits${usage.exhausted ? ` (${usage.exhausted})` : ''}` : `${name}: ${pct}%` });
	};
	add(usage.fiveHour, '5h');
	add(usage.sevenDay, '7d');
	return bars;
}

export const DESKTOP_ONLY_NOTE = 'Sign in to ChatGPT in the desktop app. Codex and profile settings are changed there.';
