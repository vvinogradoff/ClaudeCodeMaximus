// Wire DTOs — exactly as in the binding contract (10-contract.md §3/§4/§7). Optional = may be omitted (nulls are omitted).

export type AttentionKind = 'critical' | 'ask' | 'todo' | 'alert' | 'decided';
export type AttentionStatus = 'open' | 'answered' | 'resolved' | 'dismissed';
export type EffortId = 'default' | 'max' | 'high' | 'medium' | 'low';
/** The session engine (FR.33, tdcp-spec 10.A14); absent means `claude` (an older daemon). */
export type EngineId = 'claude' | 'codex';

export interface RowAttentionDto { count: number; topKind: AttentionKind }
export interface AttentionSummaryDto { openCount: number; topKind?: AttentionKind; allOpenCount: number; allOpenTopKind?: AttentionKind }

export interface SessionStateDto {
	id: string;
	running: boolean;
	streamingAlive: boolean;
	attention?: RowAttentionDto;
	favorite: boolean;
	lastActivityUtc?: string;
	lastPromptUtc?: string;
	hasDraft: boolean;
	profileName?: string;
	/** Claude Code can still resume the session (the blue square of the desktop tree); omitted until the daemon's first sweep. */
	resumable?: boolean;
	/** FR.33: the engine the session runs on; absent means `claude`. */
	engine?: EngineId;
	/** FR.1.28: how many of the session's workers (direct and nested) run right now; omitted when none does. */
	runningWorkerCount?: number;
}
export interface TreeSessionDto { id: string; name: string; workerCount: number; supervisorId?: string; state: SessionStateDto }
export interface SessionRowDto { id: string; name: string; state: SessionStateDto }
export interface SessionGroupDto { directoryId: string; directoryLabel: string; sessions: SessionRowDto[] }

/** The read-only Codex status of `client.hello` (FR.33.17, tdcp-spec 10.A14): no path, no profile data. */
export interface CodexHelloStatusDto { found: boolean; version?: string; testedVersion: string; note?: string }
export interface HelloResult {
	protocolVersion: number;
	daemonVersion: string;
	machineName: string;
	runId: string;
	serverTimeUtc: string;
	capabilities?: string[];
	/** The engines whose CLI was found, with the capability `engines`; absent from an older daemon. */
	engines?: EngineId[];
	/** Absent from an older daemon or one without the Codex engine. */
	codex?: CodexHelloStatusDto;
}

/** holdsOnlyWorkers: the group an orchestration spawned its workers into; they nest under their supervisor, so it is an empty husk the desktop tree does not show. */
export interface TreeGroupDto { id: string; name: string; isExpanded: boolean; groups: TreeGroupDto[]; sessions: TreeSessionDto[]; holdsOnlyWorkers?: boolean }
export interface TreeDirectoryDto { id: string; label: string; isExpanded: boolean; groups: TreeGroupDto[]; sessions: TreeSessionDto[] }
export interface TreeSnapshotResult { version: number; unchanged?: boolean; directories?: TreeDirectoryDto[] }
export interface TreeWorkersResult { supervisorId: string; workers: TreeSessionDto[] }

export interface TreeSearchMatchDto { id: string; strict: number; loose: number; matchedSelf: boolean; workerIds: string[] }
export interface TreeSearchResult { query: string; effectiveExact: boolean; autoExact: boolean; usedFallback: boolean; matches: TreeSearchMatchDto[] }

export interface CreateSessionResult { session: TreeSessionDto; directoryId: string; groupId?: string }

export interface RecentRowDto { id: string; name: string; directoryLabel: string; state: SessionStateDto; hasSchedules: boolean; pinned: boolean; canHide: boolean }
export interface RecentListResult { serverTimeUtc: string; sessions: RecentRowDto[] }

export type TagKind = 'linearIssue' | 'gitHubPullRequest' | 'gitHubRepository';
export interface TagRefDto { key: string; label: string; url: string; kind: TagKind }
export interface TagChipDto extends TagRefDto { sessionCount: number; selected: boolean }
export interface TagSearchResult {
	emptyReason?: 'noTagsScoped' | 'noTagsUnscoped' | 'noMatchingTags';
	chips: TagChipDto[];
	sessionsEmptyReason?: 'selectTags' | 'noSessions';
	groups: SessionGroupDto[];
}

export interface SessionTagDto { kind: TagKind; label: string; url: string; source: 'session' | 'name'; addedUtc?: string }
/** For a GPT session the effort can be a GPT-only one (`xhigh`) and the profile a ChatGPT one (`codex-default`, `codex_N`). */
export interface LastUsedSettingsDto { modelId: string; effort: string; profileId: string; profileName: string }
export interface SessionDetailDto {
	id: string;
	name: string;
	directoryId: string;
	groupId?: string;
	location: { directoryLabel: string; treePath: string };
	supervisorId?: string;
	state: SessionStateDto;
	tags: SessionTagDto[];
	attention: AttentionSummaryDto;
	lastUsed: LastUsedSettingsDto;
	isStreamingMode: boolean;
	entryCount: number;
	epoch: number;
}

export type EntryRole = 'user' | 'assistant' | 'system' | 'compaction';
/** What produced a user entry; omitted for the desktop user. */
export type EntrySource = 'scheduled' | 'orchestrated' | 'telegram' | 'web';
export interface SessionEntryDto {
	index: number;
	timestamp: string;
	role: EntryRole;
	content: string;
	truncated?: boolean;
	fullLength?: number;
	profileName?: string;
	modelId?: string;
	effort?: string;
	/** Set on a user entry that was not typed on the desktop; scheduled and orchestrated ones are shown teal. */
	source?: EntrySource;
	/** Stable id `{fileId}:{ordinal}`; omitted when the daemon has no content store. */
	entryId?: string;
}
export interface SessionEntriesResult { nodeId: string; epoch: number; total: number; entries: SessionEntryDto[]; fileId?: string; seq?: number }
export interface SessionSendResult { accepted: true; queued: boolean }

export type AttentionQuestionType = 'yesNo' | 'single' | 'multi' | 'text';
export interface AttentionQuestionDto { number: number; type: AttentionQuestionType; text: string; choices: string[]; recommended?: string; recommendedChoices: string[] }

export interface AttentionItemDto {
	id: string;
	number: number;
	kind: AttentionKind;
	title: string;
	body?: string;
	options: string[];
	recommendation?: string;
	status: AttentionStatus;
	answer?: string;
	resolutionNote?: string;
	createdUtc: string;
	updatedUtc: string;
	closedUtc?: string;
	refs?: AttentionRefDto[];
	/** The action of the item (tdcp-spec 10.A13): the elevated-run request behind an Ask. Absent on every other item; a client that does not know it shows a plain Ask. */
	action?: AttentionActionDto;
	questions?: AttentionQuestionDto[];
}
export type ElevatedRunStatus = 'pending' | 'launching' | 'running' | 'completed' | 'declined' | 'declinedAtPrompt' | 'failedToStart' | 'timedOut' | 'cancelled' | 'expired';
/** An elevated run as the relay shows it: the command whole, the working directory as a folder name only (no path leaves the PC). */
export interface ElevatedRunDto {
	id: string;
	status: ElevatedRunStatus;
	requester: { nodeId: string; name: string; shortId: string };
	topLevel?: { nodeId: string; name: string };
	reason: string;
	workingDir?: string;
	workingDirName?: string;
	command: string;
	timeoutMinutes: number;
	createdUtc: string;
	expiresUtc: string;
	startedUtc?: string;
	endedUtc?: string;
	exitCode?: number;
	cancelledBy?: 'agent' | 'user';
	note?: string;
	failure?: string;
}
export interface AttentionActionDto { type: 'elevatedRun' | string; run?: ElevatedRunDto }
export interface AttentionListResult { nodeId: string; summary: AttentionSummaryDto; items: AttentionItemDto[] }
export interface AttentionQuestionAnswer { number: number; value?: string; choices?: string[] }
export interface AttentionAnswer { itemId: string; text: string; questions?: AttentionQuestionAnswer[] }
export interface AttentionReplyResult { answeredIds: string[]; skippedIds: string[]; turnStarted: boolean }

/** `openAI` for GPT models (the daemon's camelCase enum). */
export type ModelProvider = 'anthropic' | 'ollama' | 'openAI';
export interface ModelEffortDto { id: string; description?: string }
export interface ModelOptionDto {
	id: string;
	displayName: string;
	provider?: ModelProvider | string;
	/** FR.33.2: the model's engine; absent (older daemon) means `claude`. */
	engine?: EngineId;
	/** FR.33.3: the model's own efforts, without `default`; absent means the global `efforts`. */
	efforts?: ModelEffortDto[];
	/** Only `true`, on Codex's default GPT model. */
	isDefault?: boolean;
	/** Codex's upgrade hint: shown as "Codex suggests <upgrade>". */
	upgrade?: string;
}
export interface ProfileOptionDto {
	id: string;
	displayName: string;
	/** FR.33.5: the profile's engine; absent means `claude`. */
	engine?: EngineId;
	/** The ChatGPT plan (`plus`, `pro`, ...) of a ChatGPT profile. */
	plan?: string;
}
export interface CatalogResult {
	models: ModelOptionDto[];
	efforts: string[];
	profiles: ProfileOptionDto[];
	/** Hosts whose https images/video auto-load in the transcript (`host` or `*.suffix`). Optional: older daemons omit it. */
	mediaPreviewHosts?: string[];
	/** FR.33.2: the disabled picker line when Codex was found but there are no GPT models (`GPT models: sign in to ChatGPT in Settings > Profiles`). */
	gptHint?: string;
}

// ---- Focus references, file preview, attachments (docs/features/focus-references-attachments-previews.md) ----
export interface AttentionEntryRefDto { kind: 'entry'; label?: string; nodeId: string; fileId: string; entryId: string; offset: number; length: number }
export interface AttentionFileRefDto { kind: 'file'; label?: string; refId: string; name: string; path?: string; size: number; mime: string; mtimeUtc?: string }
export interface AttentionUrlRefDto { kind: 'url'; label?: string; url: string }
export type AttentionRefDto = AttentionEntryRefDto | AttentionFileRefDto | AttentionUrlRefDto;

/** file.read result; offset/length are bytes. `utf8` for text types, `base64` for everything else. */
export interface FileReadResult { name: string; mime: string; size: number; mtimeUtc?: string; offset: number; length: number; encoding: 'utf8' | 'base64'; data: string; eof: boolean }
export interface FilePreviewResult { handle: string; name: string; mime: string; size: number; mtimeUtc?: string }
/** POST /api/attachments answer. */
export interface AttachmentResult { attachmentId: string; path: string; name: string; size: number; mime: string }

export interface UsageWindowDto {
	utilization: number;
	resetsAtUtc: string;
	isStale: boolean;
	/** FR.33.10: the bar's label from the window's length (`5h`, `7d`, `<n>h`); absent from an older daemon (then 5h and 7d). */
	label?: string;
	durationMinutes?: number;
	/** The window that ran out of credits: shown in the limits-error colour. */
	isExhausted?: boolean;
}
export interface UsageResult {
	profileId: string;
	profileName: string;
	state: 'ok' | 'pending' | 'unauthorized' | 'unavailable';
	/** For a ChatGPT profile Codex's primary window, whatever its length. */
	fiveHour?: UsageWindowDto;
	/** For a ChatGPT profile Codex's secondary window; absent when the plan has none. */
	sevenDay?: UsageWindowDto;
	fetchedAtUtc?: string;
	/** `codex` for a ChatGPT profile; absent for a Claude one. */
	engine?: EngineId;
	/** Codex's `rateLimitReachedType` while the plan is out of credits. */
	exhausted?: string;
}

// Per-turn options sent with session.send / attention.reply / session.compact.
export interface TurnOptions { modelId?: string; effort?: string; profileId?: string }

// Notifications (§4) delivered over /api/events.
export interface EventFrame { method: string; params?: unknown }
export interface SessionChangedParams { state: SessionStateDto }
export interface SessionAppendedParams { nodeId: string; epoch: number }
export interface TurnCompletedParams { nodeId: string; cancelled: boolean; error?: string }
export interface AttentionChangedParams { nodeId: string; summary: AttentionSummaryDto }
export interface TagsChangedParams { nodeId?: string }
export interface UsageChangedParams { profileId: string }
export interface LinkStatusParams { online: boolean; since?: string }
