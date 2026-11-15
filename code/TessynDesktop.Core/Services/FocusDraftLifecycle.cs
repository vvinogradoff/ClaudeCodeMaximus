using System;
using System.Linq;
using Serilog;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Keeps the Focus reply drafts (FR.23.19) in step with the Attention Ledger and the session files: a draft is removed when its item is no
/// longer open (answered, resolved or dismissed, by the user or by the session) or no longer exists, and every draft of a session goes with the
/// session file (the composer draft too). Clients delete a draft themselves when they send it; this covers the closes they do not see happen.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class FocusDraftLifecycle : IDisposable
{
	private static readonly ILogger _log = Log.ForContext<FocusDraftLifecycle>();

	private readonly IDraftService _drafts;
	private readonly IAttentionService _attention;
	private readonly ISessionFileService _files;
	private readonly ISessionRegistry _registry;
	private bool _started;

	public FocusDraftLifecycle(IDraftService drafts, IAttentionService attention, ISessionFileService files, ISessionRegistry registry)
	{
		_drafts    = drafts;
		_attention = attention;
		_files     = files;
		_registry  = registry;
	}

	/// <summary>Starts listening; a second call does nothing.</summary>
	public void Start()
	{
		if (_started)
			return;

		_started = true;
		_attention.AttentionChanged += OnAttentionChanged;
		_files.SessionFileDeleted   += OnSessionFileDeleted;
	}

	public void Dispose()
	{
		if (!_started)
			return;

		_started = false;
		_attention.AttentionChanged -= OnAttentionChanged;
		_files.SessionFileDeleted   -= OnSessionFileDeleted;
	}

	private void OnAttentionChanged(object? sender, AttentionChangedEventArgs e)
	{
		try
		{
			var node = _registry.FindModelByNodeId(e.NodeId);
			if (node == null)
				return;

			var draftIds = _drafts.ListItemDraftIds(node.FileName);
			if (draftIds.Count == 0)
				return;

			var open = _attention.GetItems(e.NodeId)
				.Where(i => i.Status == AttentionStatus.Open)
				.Select(i => i.Id)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);

			foreach (var id in draftIds.Where(id => !open.Contains(id)))
				_drafts.DeleteItemDraft(node.FileName, id);
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "FocusDraftLifecycle: cleaning the reply drafts of node {NodeId} failed", e.NodeId);
		}
	}

	private void OnSessionFileDeleted(object? sender, SessionFileDeletedEventArgs e)
	{
		try
		{
			_drafts.DeleteItemDrafts(e.FileName);
			_drafts.DeleteDraft(e.FileName);
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "FocusDraftLifecycle: deleting the reply drafts of {FileName} failed", e.FileName);
		}
	}
}
