using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Infrastructure;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// The stored per-session options (Auto-Commit, Auto-Document, completion sound) of the open session, shared by the instruction toolbar and the
/// command-bar toggles. The daemon has no getter: a <c>session.setOptions</c> that changes nothing answers the current values, which is how they
/// are read when a session opens. A change shows at once and is sent; the answer replaces it unless the user changed something again meanwhile.
/// Mid-run corrections the daemon reports (FR.11.12) are kept as <see cref="Notice"/>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class SessionOptionsViewModel : ObservableObject
{
	private readonly ITessynClient _client;
	private readonly IUiDispatcher _ui;
	private readonly ILogger _logger;
	private int _pending;
	private int _token;

	[ObservableProperty]
	private string? _nodeId;

	/// <summary>The values of the open session have been read; until then the toggles are disabled and show nothing.</summary>
	[ObservableProperty]
	private bool _isKnown;

	[ObservableProperty]
	private AutoCommitSetting _autoCommit = AutoCommitSetting.NoPreference;

	[ObservableProperty]
	private bool _autoDocument;

	[ObservableProperty]
	private bool _playCompletionSound = true;

	/// <summary>The latest correction or failure text for the toolbar; empty when there is none.</summary>
	[ObservableProperty]
	private string _notice = string.Empty;

	public SessionOptionsViewModel(ITessynClient client, IUiDispatcher ui, ILogger logger)
	{
		_client = client;
		_ui = ui;
		_logger = logger;
	}

	/// <summary>Completes when the last read or change has been answered (tests).</summary>
	public Task Idle { get; private set; } = Task.CompletedTask;

	/// <summary>A session opened (or none): its options are read again; nothing of the previous one stays.</summary>
	public void Open(string? nodeId)
	{
		_token++;
		_pending = 0;
		NodeId = nodeId;
		IsKnown = false;
		AutoCommit = AutoCommitSetting.NoPreference;
		AutoDocument = false;
		PlayCompletionSound = true;
		Notice = string.Empty;
		if (nodeId != null)
			Idle = ReadAsync(nodeId, _token);
	}

	/// <summary>Re-reads the options of the open session (after a resync).</summary>
	public Task RefreshAsync()
	{
		var id = NodeId;
		return id == null ? Task.CompletedTask : Idle = ReadAsync(id, _token);
	}

	private async Task ReadAsync(string nodeId, int token)
	{
		try
		{
			var result = await _client.SessionSetOptionsAsync(nodeId);
			_ui.Post(() => Apply(result.Options, nodeId, token));
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Reading the options of {NodeId} failed", nodeId);
		}
	}

	private void Apply(SessionOptionsDto options, string nodeId, int token)
	{
		if (token != _token || nodeId != NodeId || _pending > 0)
			return;

		AutoCommit = options.AutoCommit switch { true => AutoCommitSetting.Commit, false => AutoCommitSetting.DoNotCommit, _ => AutoCommitSetting.NoPreference };
		AutoDocument = options.AutoDocument;
		PlayCompletionSound = options.PlayCompletionSound;
		IsKnown = true;
	}

	/// <summary>Changes the options that are not null; the new values show at once.</summary>
	public Task SetAsync(AutoCommitSetting? autoCommit = null, bool? autoDocument = null, bool? playCompletionSound = null)
	{
		var id = NodeId;
		if (id == null || !IsKnown)
			return Task.CompletedTask;

		Notice = string.Empty;
		if (autoCommit is { } commit)
			AutoCommit = commit;
		if (autoDocument is { } document)
			AutoDocument = document;
		if (playCompletionSound is { } sound)
			PlayCompletionSound = sound;

		return Idle = SendAsync(id, _token, autoCommit, autoDocument, playCompletionSound);
	}

	private async Task SendAsync(string nodeId, int token, AutoCommitSetting? autoCommit, bool? autoDocument, bool? sound)
	{
		_pending++;
		try
		{
			var result = await _client.SessionSetOptionsAsync(nodeId, autoCommit, autoDocument, sound);
			_ui.Post(() =>
			{
				if (token != _token || nodeId != NodeId)
					return;

				_pending--;
				if (result.Corrections is { Count: > 0 } corrections)
					Notice = string.Join(" ", corrections);

				Apply(result.Options, nodeId, token);
			});
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "session.setOptions failed for {NodeId}", nodeId);
			_ui.Post(() =>
			{
				if (token != _token || nodeId != NodeId)
					return;

				_pending--;
				Notice = Constants.ToolbarTexts.OptionsFailed + ex.Message;
				_ = ReadAsync(nodeId, token);   // show what the daemon really has
			});
		}
	}
}
