using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TessynDesktop.Models;
using Serilog;

namespace TessynDesktop.Services;

/// <summary>
/// Default <see cref="IAttentionService"/>: ledgers are loaded lazily from <see cref="AttentionLedgerStore"/>,
/// cached, and mutated under one lock. SYSTEM entries and <see cref="AttentionChanged"/> are emitted after
/// the lock is released.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class AttentionService : IAttentionService
{
	private static readonly ILogger _log = Log.ForContext<AttentionService>();

	private readonly AttentionLedgerStore _store;
	private readonly ISessionFileService  _fileService;
	private readonly TimeProvider         _time;

	private readonly object _lock = new();
	private readonly Dictionary<string, AttentionLedgerModel> _cache = new();
	private readonly Dictionary<string, string> _fileRefIndex = new(StringComparer.Ordinal);   // refId -> nodeId
	private bool _allLedgersIndexed;

	public event EventHandler<AttentionChangedEventArgs>? AttentionChanged;

	public AttentionService(AttentionLedgerStore store, ISessionFileService fileService)
		: this(store, fileService, TimeProvider.System)
	{
	}

	/// <summary>Constructor for testing: allows a controllable clock.</summary>
	public AttentionService(AttentionLedgerStore store, ISessionFileService fileService, TimeProvider time)
	{
		_store       = store;
		_fileService = fileService;
		_time        = time;
	}

	public IReadOnlyList<AttentionPostOutcomeModel> Post(SessionNodeModel node, IReadOnlyList<AttentionPostRequestModel> requests)
	{
		if (requests.Count == 0)
			return [];

		var outcomes = new List<AttentionPostOutcomeModel>();
		var entries  = new List<string>();

		lock (_lock)
		{
			var ledger = LoadLocked(node.NodeId);
			var now    = _time.GetUtcNow();

			foreach (var request in requests)
			{
				var item    = FindOpenMatch(ledger, request);
				var created = item == null;
				if (item == null)
				{
					item = new AttentionItemModel
					{
						Number     = ledger.NextNumber,
						Id         = Constants.Attention.IdPrefix + ledger.NextNumber,
						Source     = request.Source,
						CreatedUtc = now,
					};
					ledger.NextNumber++;
					ledger.Items.Add(item);
				}

				item.Kind           = request.Kind;
				item.Title          = request.Title;
				item.Body           = request.Body;
				item.Options        = request.Options.ToList();
				item.Recommendation = request.Recommendation;
				if (request.Refs.Count > 0)
				{
					item.Refs = request.Refs.Select(CopyRef).ToList();
					IndexFileRefsLocked(node.NodeId, item);
				}
				if (!string.IsNullOrWhiteSpace(request.Key))
					item.Key = request.Key;
				item.UpdatedUtc = now;

				entries.Add(BuildPostEntry(item, created));
				outcomes.Add(new AttentionPostOutcomeModel { Item = Copy(item), IsCreated = created });
			}

			SaveLocked(ledger);
		}

		AppendEntries(node, entries);
		RaiseChanged(node.NodeId);
		return outcomes;
	}

	public AttentionResolveResultModel ResolveBySession(SessionNodeModel node, string itemId, AttentionStatus status, string? note)
	{
		if (status is not (AttentionStatus.Answered or AttentionStatus.Resolved))
			throw new ArgumentOutOfRangeException(nameof(status), status, "A session can only answer or resolve an item.");

		var text = Cut(note?.Trim() ?? string.Empty, Constants.Attention.MaxNoteLength);
		AttentionItemModel snapshot;

		lock (_lock)
		{
			var ledger = LoadLocked(node.NodeId);
			var item   = FindById(ledger, itemId);
			if (item == null)
				return new AttentionResolveResultModel { Outcome = AttentionResolveOutcome.Unknown };
			if (item.Status != AttentionStatus.Open)
				return new AttentionResolveResultModel { Outcome = AttentionResolveOutcome.AlreadyClosed, Item = Copy(item) };

			var now = _time.GetUtcNow();
			item.Status     = status;
			item.UpdatedUtc = now;
			item.ClosedUtc  = now;
			if (status == AttentionStatus.Answered)
				item.Answer = text;
			else
				item.ResolutionNote = text;

			SaveLocked(ledger);
			snapshot = Copy(item);
		}

		var entry = string.Format(Constants.Attention.SystemClosedFormat, snapshot.Id, AttentionFormat.StatusName(status));
		AppendEntries(node, [text.Length == 0 ? entry : entry + " " + text]);
		RaiseChanged(node.NodeId);
		return new AttentionResolveResultModel { Outcome = AttentionResolveOutcome.Closed, Item = snapshot };
	}

	public IReadOnlyList<AttentionItemModel> GetItems(string nodeId)
	{
		lock (_lock)
			return AttentionOrdering.Sort(LoadLocked(nodeId).Items).Select(Copy).ToList();
	}

	public AttentionSummaryModel GetSummary(string nodeId)
	{
		lock (_lock)
		{
			var open = LoadLocked(nodeId).Items
				.Where(i => i.Status == AttentionStatus.Open)
				.ToList();
			var counted = open.Where(i => i.Kind != AttentionKind.Decided).ToList();

			return new AttentionSummaryModel
			{
				OpenCount      = counted.Count,
				TopKind        = counted.Count == 0 ? null : counted.Min(i => i.Kind),
				AllOpenCount   = open.Count,
				AllOpenTopKind = open.Count == 0 ? null : open.Min(i => i.Kind),
			};
		}
	}

	public void MarkAnsweredByUser(string nodeId, IReadOnlyList<AttentionAnswerModel> answers)
	{
		var changed = false;

		lock (_lock)
		{
			var ledger = LoadLocked(nodeId);
			var now    = _time.GetUtcNow();

			foreach (var answer in answers)
			{
				var item = FindById(ledger, answer.ItemId);
				if (item is not { Status: AttentionStatus.Open })
					continue;

				item.Status     = AttentionStatus.Answered;
				item.Answer     = answer.Answer;
				item.UpdatedUtc = now;
				item.ClosedUtc  = now;
				changed         = true;
			}

			if (changed)
				SaveLocked(ledger);
		}

		if (changed)
			RaiseChanged(nodeId);
	}

	public bool CloseByUser(SessionNodeModel node, string itemId)
	{
		string entry;

		lock (_lock)
		{
			var ledger = LoadLocked(node.NodeId);
			var item   = FindById(ledger, itemId);
			if (item is not { Status: AttentionStatus.Open })
				return false;

			var now = _time.GetUtcNow();
			item.Status                = AttentionStatus.Dismissed;
			item.IsUserCloseUnreported = true;
			item.UpdatedUtc            = now;
			item.ClosedUtc             = now;

			SaveLocked(ledger);
			entry = string.Format(Constants.Attention.SystemDismissedByUserFormat, item.Id);
		}

		AppendEntries(node, [entry]);
		RaiseChanged(node.NodeId);
		return true;
	}

	public IReadOnlyList<AttentionItemModel> TakeUnreportedUserClosures(string nodeId)
	{
		lock (_lock)
		{
			var ledger = LoadLocked(nodeId);
			var taken  = ledger.Items
				.Where(i => i.IsUserCloseUnreported)
				.OrderBy(i => i.ClosedUtc)
				.ThenBy(i => i.Number)
				.ToList();
			if (taken.Count == 0)
				return [];

			var copies = taken.Select(Copy).ToList();
			foreach (var item in taken)
				item.IsUserCloseUnreported = false;

			SaveLocked(ledger);
			return copies;
		}
	}

	public (string NodeId, AttentionRefModel Ref)? FindFileRef(string refId)
	{
		lock (_lock)
		{
			if (!_allLedgersIndexed)
			{
				// Once: every ledger on disk, so a handle issued before a restart still resolves.
				foreach (var nodeId in _store.EnumerateNodeIds())
				{
					foreach (var item in LoadLocked(nodeId).Items)
						IndexFileRefsLocked(nodeId, item);
				}

				_allLedgersIndexed = true;
			}

			if (!_fileRefIndex.TryGetValue(refId, out var owner))
				return null;

			var found = LoadLocked(owner).Items
				.SelectMany(i => i.Refs)
				.FirstOrDefault(r => string.Equals(r.RefId, refId, StringComparison.Ordinal));
			return found == null ? null : (owner, CopyRef(found));
		}
	}

	private void IndexFileRefsLocked(string nodeId, AttentionItemModel item)
	{
		foreach (var r in item.Refs)
		{
			if (r.Kind == Constants.Attention.RefKindFile && !string.IsNullOrEmpty(r.RefId))
				_fileRefIndex[r.RefId] = nodeId;
		}
	}

	// ── Helpers ────────────────────────────────────────────────────────────────

	private AttentionLedgerModel LoadLocked(string nodeId)
	{
		if (_cache.TryGetValue(nodeId, out var cached))
			return cached;

		var ledger = _store.Load(nodeId);
		_cache[nodeId] = ledger;
		return ledger;
	}

	/// <summary>Saves the ledger; on failure the cache entry is dropped so the next call reloads the file's real state.</summary>
	private void SaveLocked(AttentionLedgerModel ledger)
	{
		try
		{
			_store.Save(ledger);
		}
		catch
		{
			_cache.Remove(ledger.NodeId);
			throw;
		}
	}

	private static AttentionItemModel? FindOpenMatch(AttentionLedgerModel ledger, AttentionPostRequestModel request)
	{
		var open = ledger.Items.Where(i => i.Status == AttentionStatus.Open).ToList();

		if (!string.IsNullOrWhiteSpace(request.Key))
		{
			var byKey = open.FirstOrDefault(i =>
				!string.IsNullOrWhiteSpace(i.Key) && string.Equals(i.Key, request.Key, StringComparison.OrdinalIgnoreCase));
			if (byKey != null)
				return byKey;
		}

		var title = NormalizeTitle(request.Title);
		return open.FirstOrDefault(i => string.Equals(NormalizeTitle(i.Title), title, StringComparison.OrdinalIgnoreCase));
	}

	private static string NormalizeTitle(string title) =>
		string.Join(' ', title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

	private static AttentionItemModel? FindById(AttentionLedgerModel ledger, string itemId)
	{
		var id = itemId.Trim();
		return ledger.Items.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>FR.23.8 layout: first line, then body, Options and Recommendation lines when present. ASCII separators only.</summary>
	private static string BuildPostEntry(AttentionItemModel item, bool created)
	{
		var sb = new StringBuilder(string.Format(
			created ? Constants.Attention.SystemCreatedFormat : Constants.Attention.SystemUpdatedFormat,
			item.Id, AttentionFormat.KindName(item.Kind), item.Title));

		if (item.Body.Length > 0)
			sb.Append('\n').Append(item.Body);
		if (item.Options.Count > 0)
			sb.Append('\n').Append(Constants.Attention.SystemOptionsPrefix)
				.Append(string.Join(Constants.Attention.SystemOptionsSeparator, item.Options));
		if (!string.IsNullOrEmpty(item.Recommendation))
			sb.Append('\n').Append(Constants.Attention.SystemRecommendationPrefix).Append(item.Recommendation);

		return sb.ToString();
	}

	private void AppendEntries(SessionNodeModel node, IEnumerable<string> entries)
	{
		foreach (var entry in entries)
		{
			try
			{
				_fileService.AppendMessage(node.FileName, Constants.SessionFile.RoleSystem, entry);
			}
			catch (Exception ex)
			{
				_log.Warning(ex, "AttentionService: failed to append SYSTEM entry for node {NodeId}", node.NodeId);
			}
		}
	}

	private void RaiseChanged(string nodeId)
	{
		try
		{
			AttentionChanged?.Invoke(this, new AttentionChangedEventArgs(nodeId));
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "AttentionService: AttentionChanged subscriber failed for node {NodeId}", nodeId);
		}
	}

	private static string Cut(string text, int maxLength) => text.Length > maxLength ? text[..maxLength] : text;

	private static AttentionItemModel Copy(AttentionItemModel item) => new()
	{
		Id                    = item.Id,
		Number                = item.Number,
		Kind                  = item.Kind,
		Title                 = item.Title,
		Body                  = item.Body,
		Options               = item.Options.ToList(),
		Recommendation        = item.Recommendation,
		Key                   = item.Key,
		Status                = item.Status,
		Source                = item.Source,
		Answer                = item.Answer,
		ResolutionNote        = item.ResolutionNote,
		IsUserCloseUnreported = item.IsUserCloseUnreported,
		CreatedUtc            = item.CreatedUtc,
		UpdatedUtc            = item.UpdatedUtc,
		ClosedUtc             = item.ClosedUtc,
		Refs                  = item.Refs.Select(CopyRef).ToList(),
	};

	private static AttentionRefModel CopyRef(AttentionRefModel r) => new()
	{
		Kind = r.Kind, Label = r.Label, NodeId = r.NodeId, FileId = r.FileId, EntryId = r.EntryId, Offset = r.Offset, Length = r.Length,
		RefId = r.RefId, Name = r.Name, RelativePath = r.RelativePath, AbsolutePath = r.AbsolutePath, Size = r.Size, Mime = r.Mime,
		MtimeUtc = r.MtimeUtc, Url = r.Url,
	};
}
