// Wire DTOs — exactly as in the binding contract (10-contract.md §3/§4/§7). Optional = may be omitted (nulls are omitted).

export type AttentionKind = 'critical' | 'ask' | 'todo' | 'alert' | 'decided';
export type AttentionStatus = 'open' | 'answered' | 'resolved' | 'dismissed';
export type EffortId = 'default' | 'max' | 'high' | 'medium' | 'low';

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
}
export interface TreeSessionDto { id: string; name: string; workerCount: number; supervisorId?: string; state: SessionStateDto }
export interface SessionRowDto { id: string; name: string; state: SessionStateDto }
export interface SessionGroupDto { directoryId: string; directoryLabel: string; sessions: SessionRowDto[] }

export interface HelloResult { protocolVersion: number; daemonVersion: string; machineName: string; runId: string; serverTimeUtc: string }

export interface TreeGroupDto { id: string; name: string; isExpanded: boolean; groups: TreeGroupDto[]; sessions: TreeSessionDto[] }
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
export interface LastUsedSettingsDto { modelId: string; effort: EffortId; profileId: string; profileName: string }
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
}
export interface SessionEntriesResult { nodeId: string; epoch: number; total: number; entries: SessionEntryDto[] }
export interface SessionSendResult { accepted: true; queued: boolean }

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
}
export interface AttentionListResult { nodeId: string; summary: AttentionSummaryDto; items: AttentionItemDto[] }
export interface AttentionAnswer { itemId: string; text: string }
export interface AttentionReplyResult { answeredIds: string[]; skippedIds: string[]; turnStarted: boolean }

export interface ModelOptionDto { id: string; displayName: string; provider?: string }
export interface ProfileOptionDto { id: string; displayName: string }
export interface CatalogResult { models: ModelOptionDto[]; efforts: string[]; profiles: ProfileOptionDto[] }

export interface UsageWindowDto { utilization: number; resetsAtUtc: string; isStale: boolean }
export interface UsageResult {
	profileId: string;
	profileName: string;
	state: 'ok' | 'pending' | 'unauthorized' | 'unavailable';
	fiveHour?: UsageWindowDto;
	sevenDay?: UsageWindowDto;
	fetchedAtUtc?: string;
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
