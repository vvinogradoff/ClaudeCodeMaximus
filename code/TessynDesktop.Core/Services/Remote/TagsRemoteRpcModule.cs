using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// Remote method of the Tags tab: <c>tags.search</c> (contract 3.8; FR.24.11–FR.24.13). Stateless: the client's selection arrives with
/// every call. The index is built from a copy of the tree with <see cref="SessionTagIndexBuilder"/> (workers rolled up onto their top-level session).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TagsRemoteRpcModule : IRemoteRpcModule
{
	private readonly IRemoteTreeReader _reader;
	private readonly ISessionTagService _tags;
	private readonly ISessionActivityService _activity;
	private readonly ISessionStateBuilder _states;
	private readonly IDirectoryLabelService _labels;

	public TagsRemoteRpcModule(
		IRemoteTreeReader reader,
		ISessionTagService tags,
		ISessionActivityService activity,
		ISessionStateBuilder states,
		IDirectoryLabelService labels)
	{
		_reader   = reader;
		_tags     = tags;
		_activity = activity;
		_states   = states;
		_labels   = labels;
	}

	/// <inheritdoc />
	public void Register(TdcpMethodRegistry registry) =>
		registry.AddSync(Constants.Remote.Methods.TagsSearch, Search, remoteCallable: true);

	private object? Search(JsonNode? @params)
	{
		var text     = TdcpParams.OptionalString(@params, "text") ?? string.Empty;
		var scope    = TdcpParams.OptionalString(@params, "scopeNodeId");
		var selected = ParseSelected(TdcpParams.Find(@params, "selected"));

		var snapshot = _reader.Snapshot();

		// Scope = the Directory node of the scope session's top-level session (a worker scopes like its supervisor).
		string? directoryPath = null;
		if (!string.IsNullOrEmpty(scope))
		{
			var scopeSession = snapshot.Find(scope) ?? throw new TdcpException("Session not found");
			directoryPath = snapshot.ResolveTopLevel(scopeSession).DirectoryPath;
		}

		var indexSessions = snapshot.AllSessions
			.Select(s => new SessionTagIndexSessionModel(s.NodeId, s.Name, s.SupervisorNodeId, s.DirectoryPath))
			.ToList();
		var index = SessionTagIndexBuilder.Build(indexSessions, s => _tags.GetTags(s.NodeId, s.Name), directoryPath);

		DateTimeOffset? LastActivityOf(string nodeId)
		{
			var record = snapshot.Find(nodeId);
			return record == null ? null : _activity.GetActivity(record.Node).LastActivityUtc;
		}

		var chips = BuildChips(index, selected, text, LastActivityOf);

		TagEmptyReason? emptyReason = null;
		if (chips.Count == 0)
		{
			emptyReason = index.Count == 0
				? (scope != null ? TagEmptyReason.NoTagsScoped : TagEmptyReason.NoTagsUnscoped)
				: TagEmptyReason.NoMatchingTags;
		}

		var groups = new List<SessionGroupDto>();
		if (selected.Count > 0)
		{
			var ids = SessionTagSearch.SessionsWithAnyTag(index, selected.Keys);
			groups = SessionGroupBuilder.Build(
				snapshot,
				_labels.GetLabel,
				ids.Contains,
				r => _activity.GetActivity(r.Node),
				r => _states.Build(r.Node)).ToList();
		}

		TagSessionsEmptyReason? sessionsEmptyReason = selected.Count == 0
			? TagSessionsEmptyReason.SelectTags
			: groups.Count == 0 ? TagSessionsEmptyReason.NoSessions : null;

		return new TagSearchResult(emptyReason, chips, sessionsEmptyReason, groups);
	}

	/// <summary>Selected tags first, even when they no longer match the text or left the index (stub with zero sessions), then the other matches; each part in FR.24.11 order.</summary>
	private static List<TagChipDto> BuildChips(
		IReadOnlyList<SessionTagUsageModel> index,
		Dictionary<string, TagRefDto> selected,
		string text,
		Func<string, DateTimeOffset?> lastActivityOf)
	{
		var byKey = new Dictionary<string, SessionTagUsageModel>(StringComparer.OrdinalIgnoreCase);
		foreach (var usage in index)
			byKey[usage.Tag.Key] = usage;

		var selectedUsages = selected
			.Select(kv => byKey.TryGetValue(kv.Key, out var u) ? u : StubFor(kv.Value))
			.ToList();
		var otherUsages = index
			.Where(u => !selected.ContainsKey(u.Tag.Key) && SessionTagSearch.Matches(u.Tag, text))
			.ToList();

		return SessionTagSearch.Order(selectedUsages, lastActivityOf)
			.Concat(SessionTagSearch.Order(otherUsages, lastActivityOf))
			.Select(u => new TagChipDto(
				u.Tag.Key,
				u.Tag.Label,
				SessionTagUrlPolicy.IsOpenable(u.Tag.Url) ? u.Tag.Url : string.Empty,
				u.Tag.Kind,
				u.NodeIds.Count,
				selected.ContainsKey(u.Tag.Key)))
			.ToList();
	}

	private static SessionTagUsageModel StubFor(TagRefDto reference) => new()
	{
		Tag = new SessionTagModel { Kind = reference.Kind, Key = reference.Key, Label = reference.Label ?? reference.Key, Url = reference.Url ?? string.Empty },
	};

	/// <summary>The client's selection keyed by tag key (case-insensitive, first occurrence wins, insertion order kept).</summary>
	private static Dictionary<string, TagRefDto> ParseSelected(JsonNode? node)
	{
		var result = new Dictionary<string, TagRefDto>(StringComparer.OrdinalIgnoreCase);
		if (node == null)
			return result;

		if (node is not JsonArray array)
			throw new TdcpInvalidParamsException("'selected' must be an array");

		foreach (var item in array)
		{
			TagRefDto? reference;
			try
			{
				reference = item.Deserialize<TagRefDto>(TdcpJsonOptions.Default);
			}
			catch (JsonException)
			{
				throw new TdcpInvalidParamsException("'selected' holds an invalid tag");
			}

			if (reference == null || string.IsNullOrWhiteSpace(reference.Key))
				throw new TdcpInvalidParamsException("every selected tag needs a 'key'");

			result.TryAdd(reference.Key, reference);
		}

		return result;
	}
}
