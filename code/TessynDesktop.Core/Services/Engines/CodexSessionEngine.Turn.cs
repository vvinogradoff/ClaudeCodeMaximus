using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Codex;
using TessynDesktop.Codex.Events;
using TessynDesktop.Codex.Protocol;
using TessynDesktop.Models;
using TessynDesktop.Models.Engines;
using TessynDesktop.Models.Turns;

namespace TessynDesktop.Services;

public sealed partial class CodexSessionEngine
{
	/// <summary>
	/// One Codex turn (FR.33.7, FR.33.8, FR.33.11): the same rules in both pipelines (<see cref="EngineTurnRequest.Mode"/> is not used, and
	/// <see cref="EngineTurnRequest.Persistent"/> does not apply). Failures before or during the turn end it with a SYSTEM entry through
	/// <see cref="IEngineEventSink.TurnEnded"/>; a Stop ends it with <see cref="OperationCanceledException"/>.
	/// </summary>
	public async Task RunTurnAsync(EngineTurnRequest request, IEngineEventSink sink, CancellationToken cancellationToken)
	{
		var node     = request.Node;
		var settings = request.Settings;
		WatchRenames();

		using var turnCancel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		var profile = _profiles.Select(node, settings);

		// The thread is known to the idle unload before it is opened, so an unload that is already due skips it (FR.33.6).
		var running = new RunningTurn(turnCancel, profile.ProfileId) { ThreadId = node.CodexThreadId };
		_running[node.NodeId] = running;
		if (node.CodexThreadId is { } current)
			CancelIdleUnload(current);

		var ct = turnCancel.Token;
		CodexAppServerHost? usedHost = null;
		JsonObject? config = null;

		try
		{
			CodexAppServerHost host;
			try
			{
				host = _pool.GetHost(profile);
			}
			catch (Exception ex) when (ex is CodexNotFoundException or InvalidOperationException)
			{
				_log.Warning("Codex session {NodeId} can't run: {Problem}", node.NodeId, ex.Message);
				EndWithError(sink, string.Format(Constants.Codex.Texts.StartFailedFormat, ex.Message));
				return;
			}

			usedHost = host;
			config   = request.Preparation is CodexTurnPreparation prepared ? prepared.Config : await _config.BuildAsync(node, ct).ConfigureAwait(false);

			// FR.33.12: queued corrections go with this turn; they leave the queue only once turn/start was accepted.
			var corrections = PeekCorrections(node.NodeId);
			var seedText    = request.AugmentedPrompt + corrections.Text;

			// FR.33.5: the turn's ChatGPT profile becomes the session's pin. A move from another ChatGPT profile detaches the thread (threads are
			// not moved between Codex homes), and so does a switch applied under the lock after the caller read the conversation: either way the
			// new thread is seeded from the transcript.
			var prompt = request.Prompt + corrections.Text;
			var moved  = RecordPin(node, profile, settings);
			if (moved || (request.ConversationId is not null && node.CodexThreadId is null))
				prompt = InstructionBlockBuilder.BuildContextPreamble(_files.ReadEntries(node.FileName), seedText);

			for (var attempt = 0; ; attempt++)
			{
				var opened = await OpenThreadAsync(host, node, settings, config, prompt, seedText, sink, ct).ConfigureAwait(false);
				if (opened is not { } thread)
					return;

				running.ThreadId = thread.ThreadId;
				try
				{
					await RunOnThreadAsync(host, profile, node, thread.ThreadId, thread.Text, settings, request, sink, ct,
						() => CommitCorrections(node.NodeId, corrections.Count)).ConfigureAwait(false);
					return;
				}
				catch (CodexThreadException ex) when (ex.Kind == CodexThreadErrorKind.NotLoaded && attempt == 0)
				{
					// FR.33.7: the host no longer has the thread loaded. It is opened again (a resume, with the gone and active-writer rules) and the
					// turn is sent once more.
					_log.Information("Codex thread {ThreadId} of session {NodeId} was no longer loaded; resuming it and sending the turn again", thread.ThreadId, node.NodeId);
					prompt = thread.Text;
				}
			}
		}
		catch (Exception ex) when (ct.IsCancellationRequested && ex is not OperationCanceledException)
		{
			// A call that failed because the turn was stopped while it ran: the turn ends as cancelled.
			_log.Debug("A Codex call of session {NodeId} failed after Stop: {Error}", node.NodeId, _config.RedactText(ex.Message, config, node.AgentToken));
			throw new OperationCanceledException(ct);
		}
		catch (CodexHostCrashedException crash)
		{
			EndWithError(sink, HostStoppedText(crash.Crash));
		}
		catch (Exception ex) when (ex is CodexRpcException or TimeoutException or CodexConnectionLostException)
		{
			// Codex may quote a config value in its error ("failed to load configuration: …"): no token reaches the log or the transcript.
			var reason = _config.RedactText(ex.Message, config, node.AgentToken);
			_log.Warning("A Codex turn of session {NodeId} could not run ({Type}): {Error}", node.NodeId, ex.GetType().Name, reason);
			EndWithError(sink, string.Format(Constants.Codex.Texts.StartFailedFormat, reason));
		}
		finally
		{
			_running.TryRemove(new KeyValuePair<string, RunningTurn>(node.NodeId, running));
			if (usedHost is not null && running.ThreadId is { } threadId)
			{
				if (_unloadAfterTurn.TryRemove(threadId, out _))
					_ = UnsubscribeQuietlyAsync(node, threadId);
				else
					ScheduleIdleUnload(usedHost, threadId);
			}
		}
	}

	/// <summary>Pins the session to the turn's ChatGPT profile (when the pipeline resolved one). True when it moved from another ChatGPT profile and its thread was detached.</summary>
	private bool RecordPin(SessionNodeModel node, CodexProfileRef profile, TurnSettings settings)
	{
		if (settings.CodexProfile is null)
			return false;

		string? previous;
		lock (_appSettings.SyncRoot)
		{
			previous = node.ProfileId;
			if (string.Equals(previous, profile.ProfileId, StringComparison.OrdinalIgnoreCase))
				return false;
		}

		var switched = _naming.IsCodexProfileId(previous) && !string.IsNullOrEmpty(node.CodexThreadId) && DetachConversation(node);
		lock (_appSettings.SyncRoot)
		{
			node.ProfileId   = profile.ProfileId;
			node.ProfileName = _naming.IsChatGptName(settings.ProfileDisplayName)
				? settings.ProfileDisplayName
				: _profileService?.DisplayNameOf(profile.ProfileId) ?? node.ProfileName;
			_appSettings.Save();
		}

		if (switched)
			_log.Information("Codex session {NodeId} moved from profile {Old} to {New}; its next thread is seeded from the transcript", node.NodeId, previous, profile.ProfileId);
		return switched;
	}

	/// <summary>FR.33.6: a thread with no turn for 15 minutes is unloaded, so its host can idle out; its next turn resumes it.</summary>
	private void ScheduleIdleUnload(CodexAppServerHost host, string threadId)
	{
		ITimer? timer = null;
		timer = _time.CreateTimer(_ => _ = UnloadIdleThreadAsync(host, threadId, timer!), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
		var old = _idleUnloads.GetValueOrDefault(threadId);
		_idleUnloads[threadId] = timer;
		old?.Dispose();
		timer.Change(Constants.Codex.ThreadIdleUnload, Timeout.InfiniteTimeSpan);
	}

	private void CancelIdleUnload(string threadId)
	{
		if (_idleUnloads.TryRemove(threadId, out var timer))
			timer.Dispose();
	}

	private async Task UnloadIdleThreadAsync(CodexAppServerHost host, string threadId, ITimer timer)
	{
		// Only the timer still registered for the thread may unload it: a turn that started meanwhile cancelled (removed) it, and a newer turn
		// replaced it. A turn that is opening the thread is in _running already, with its thread id.
		if (!_idleUnloads.TryRemove(new KeyValuePair<string, ITimer>(threadId, timer)))
			return;
		if (_running.Values.Any(r => r.ThreadId == threadId) || !host.IsThreadLoaded(threadId))
			return;

		try
		{
			await host.UnsubscribeThreadAsync(threadId).ConfigureAwait(false);
			_log.Information("Codex thread {ThreadId} had no turn for {Idle}; it is unloaded until its next turn", threadId, Constants.Codex.ThreadIdleUnload);
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "Unloading idle Codex thread {ThreadId} failed", threadId);
		}
	}

	/// <summary>
	/// The thread the turn runs on, and the text it sends: a new thread on the first turn; the stored one, resumed once per host process; or,
	/// when the stored one is gone, a new thread seeded with the context preamble (FR.11.11). Null when the turn already ended (an active writer).
	/// </summary>
	private async Task<(string ThreadId, string Text)?> OpenThreadAsync(CodexAppServerHost host, SessionNodeModel node, TurnSettings settings,
		JsonObject config, string text, string seedText, IEngineEventSink sink, CancellationToken ct)
	{
		var threadId = node.CodexThreadId;
		if (string.IsNullOrEmpty(threadId))
			return (await StartThreadAsync(host, node, settings, config, ct).ConfigureAwait(false), text);

		if (host.IsThreadLoaded(threadId))
			return (threadId, text);

		try
		{
			await ResumeAsync(host, threadId, config, ct).ConfigureAwait(false);
			return (threadId, text);
		}
		catch (CodexThreadException ex) when (ex.Kind == CodexThreadErrorKind.ThreadGone)
		{
			// The Codex counterpart of Claude's "No conversation found" (FR.33.7): reseed from the transcript.
			_log.Information("Codex thread {ThreadId} of session {NodeId} is gone ({Reason}); a new thread is seeded from the transcript", threadId, node.NodeId, ex.Message);
			DetachConversation(node);
			sink.Retrying(EngineRetryKind.LostConversation);
			var seeded = InstructionBlockBuilder.BuildContextPreamble(_files.ReadEntries(node.FileName), seedText);
			var newThread = await StartThreadAsync(host, node, settings, config, ct).ConfigureAwait(false);
			sink.RetryRunning();
			return (newThread, seeded);
		}
		catch (CodexThreadException ex) when (ex.Kind == CodexThreadErrorKind.ActiveWriter)
		{
			// Another Codex process on the same CODEX_HOME has it open (the User's CLI on Codex Default). The thread is kept.
			_log.Warning("Codex thread {ThreadId} of session {NodeId} is open in another Codex process; the turn is not sent", threadId, node.NodeId);
			EndWithError(sink, Constants.Codex.Texts.ActiveWriter);
			return null;
		}
	}

	/// <summary><c>thread/start</c> (FR.33.7). The id is stored and saved at once, before any event of the turn; the first one also becomes the ExternalId.</summary>
	private async Task<string> StartThreadAsync(CodexAppServerHost host, SessionNodeModel node, TurnSettings? settings, JsonObject config, CancellationToken ct)
	{
		var result = await host.StartThreadAsync(new CodexThreadStartParams
		{
			Cwd            = node.WorkingDirectory,
			Model          = ModelOf(settings),
			ApprovalPolicy = Constants.Codex.Values.ApprovalPolicyNever,
			Sandbox        = Constants.Codex.Values.SandboxDangerFullAccess,
			ServiceName    = Constants.Codex.Values.ServiceName,
			ThreadSource   = Constants.Codex.Values.ThreadSource,
			Config         = config,
		}, ct).ConfigureAwait(false);

		var threadId = result.Thread.Id;
		lock (_appSettings.SyncRoot)
		{
			node.CodexThreadId = threadId;
			if (string.IsNullOrEmpty(node.ExternalId))
				node.ExternalId = threadId;
			_appSettings.Save();
		}

		_log.Information("Codex thread {ThreadId} started for session {NodeId} on profile {Profile}", threadId, node.NodeId, host.ProfileId);
		return threadId;
	}

	/// <summary><c>thread/resume { excludeTurns: true }</c> with the thread's config (FR.33.7, FR.33.9: a new token or registry list applies).</summary>
	private Task ResumeAsync(CodexAppServerHost host, string threadId, JsonObject config, CancellationToken ct) =>
		host.ResumeThreadAsync(new CodexThreadResumeParams { ThreadId = threadId, ExcludeTurns = true, Config = config }, ct);

	/// <summary>
	/// <c>turn/start</c> and the turn's events until <c>turn/completed</c> (FR.33.8, FR.33.11). A thread that is no longer loaded is resumed once
	/// and the turn sent again. <c>turn/start</c> itself is never cancelled half-way, so a Stop always knows the turn id it interrupts.
	/// </summary>
	private async Task RunOnThreadAsync(CodexAppServerHost host, CodexProfileRef profile, SessionNodeModel node, string threadId, string text,
		TurnSettings settings, EngineTurnRequest request, IEngineEventSink sink, CancellationToken ct, Action onAccepted)
	{
		var turnParams = new CodexTurnStartParams
		{
			ThreadId            = threadId,
			Input               = BuildInput(node, text, settings),
			Model               = ModelOf(settings),
			Effort              = EffortOf(settings),
			Summary             = Constants.Codex.Values.SummaryConcise,
			ClientUserMessageId = request.UserEntryId ?? Guid.NewGuid().ToString(),
		};

		long inputTokens = 0, outputTokens = 0;
		using var listener = new TurnListener(host, threadId, e => Dispatch(e, sink, ref inputTokens, ref outputTokens));
		ct.ThrowIfCancellationRequested();

		// A NotLoaded refusal goes to the caller, which opens the thread again (FR.33.7) and sends the turn once more.
		var started = await host.StartTurnAsync(turnParams).ConfigureAwait(false);
		onAccepted();

		var turnId = started.Turn.Id;
		listener.SetTurnId(turnId);

		TurnFinish finish;
		try
		{
			finish = await listener.Finished.WaitAsync(ct).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested)
		{
			await InterruptTurnAsync(host, node, threadId, turnId, listener, sink).ConfigureAwait(false);
			throw;
		}

		if (finish.Crash is { } crash)
		{
			EndWithError(sink, HostStoppedText(crash));
			return;
		}

		if (finish.Stopped is { } reason)
		{
			EndWithError(sink, StoppedText(reason));
			return;
		}

		var completion = finish.Completion!;
		var now        = _time.GetUtcNow();
		switch (completion.Outcome)
		{
			case CodexTurnOutcome.Completed:
				_profileService?.SetAuthError(profile.ProfileId, false);
				sink.TurnEnded(new EngineTurnEnd(true, null, false, false, threadId, ToInt(completion.InputTokens), ToInt(completion.OutputTokens), 0, now,
					completion.AssistantText));
				break;

			case CodexTurnOutcome.Failed:
				// FR.33.13: structured only. Auth sets the session's and the profile's auth error; a limit adds the reset time of the exhausted window.
				var error = completion.Error;
				var errorText = error?.SystemText ?? string.Format(Constants.Codex.Texts.ErrorFormat, completion.Outcome);
				if (error?.Kind == CodexErrorKind.Auth)
					_profileService?.SetAuthError(profile.ProfileId, true);
				if (error?.Kind == CodexErrorKind.Limit && _usage is not null)
					errorText += _usage.LimitResetSuffix(profile.ProfileId);
				sink.TurnEnded(new EngineTurnEnd(false, errorText, error?.Kind == CodexErrorKind.Auth, error?.Kind == CodexErrorKind.Limit, threadId,
					ToInt(completion.InputTokens), ToInt(completion.OutputTokens), 0, now, completion.AssistantText));
				break;

			default:
				EndWithError(sink, Constants.Codex.Texts.InterruptedElsewhere);
				break;
		}

		SyncThreadName(host, profile, node, threadId);
	}

	/// <summary>
	/// Stop (FR.33.8): <c>turn/interrupt</c>, then up to <see cref="Constants.Codex.StopConfirmTimeout"/> for <c>turn/completed</c>. Its partial
	/// agentMessage has been written by then (the mapper's flush, with <c>[interrupted]</c>). Without it, a SYSTEM entry and a Warning, and the thread
	/// is unsubscribed; the host is not killed, because other sessions share it.
	/// </summary>
	private async Task InterruptTurnAsync(CodexAppServerHost host, SessionNodeModel node, string threadId, string turnId, TurnListener listener, IEngineEventSink sink)
	{
		_log.Information("Stopping Codex turn {TurnId} of session {NodeId}", turnId, node.NodeId);
		_ = InterruptQuietlyAsync(host, threadId, turnId);

		var timeout   = Task.Delay(Constants.Codex.StopConfirmTimeout, _time);
		var confirmed = await Task.WhenAny(listener.Finished, timeout).ConfigureAwait(false) == listener.Finished;
		if (confirmed)
			return;

		_log.Warning("Codex did not confirm the interrupt of turn {TurnId} on thread {ThreadId} within {Timeout}; the turn ends as cancelled and the thread is unsubscribed",
			turnId, threadId, Constants.Codex.StopConfirmTimeout);
		sink.Error(new EngineError(Constants.Codex.Texts.StopNotConfirmed, IsAuthError: false, IsLimitError: false, _time.GetUtcNow()));
		try
		{
			await host.UnsubscribeThreadAsync(threadId).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "Unsubscribing Codex thread {ThreadId} after an unconfirmed Stop failed", threadId);
		}
	}

	private async Task InterruptQuietlyAsync(CodexAppServerHost host, string threadId, string turnId)
	{
		try
		{
			await host.InterruptTurnAsync(threadId, turnId).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "turn/interrupt for Codex turn {TurnId} on thread {ThreadId} failed", turnId, threadId);
		}
	}

	/// <summary>One mapped event to the seam's sink (FR.33.11). Text deltas stay unreported: the completed message carries the text (as on Claude).</summary>
	private void Dispatch(CodexTurnEvent turnEvent, IEngineEventSink sink, ref long inputTokens, ref long outputTokens)
	{
		var now = _time.GetUtcNow();
		switch (turnEvent)
		{
			case CodexThinkingDeltaEvent thinking:
				sink.Thinking(thinking.Text);
				break;
			case CodexAssistantMessageEvent message:
				sink.AssistantText(new EngineAssistantText(message.Text, now, IsLimitError: false));
				break;
			case CodexToolStartedEvent started:
				sink.ToolStarted(started.ItemId, ToolName(started.Kind), started.Summary);
				break;
			case CodexToolFinishedEvent finished:
				sink.ToolFinished(finished.ItemId, finished.Summary, isError: !finished.Succeeded);
				break;
			case CodexProgressEvent progress:
				sink.Progress(progress.Text, now);
				break;
			case CodexCompactionEvent:
				sink.Compacted(now);
				break;
			case CodexUsageEvent usage:
				inputTokens  += usage.InputTokens;
				outputTokens += usage.OutputTokens;
				sink.Usage(ToInt(inputTokens), ToInt(outputTokens));
				break;
		}
	}

	/// <summary>Gives the thread the session's name (FR.33.7) when it differs from the name last set; failures are only logged.</summary>
	private void SyncThreadName(CodexAppServerHost host, CodexProfileRef profile, SessionNodeModel node, string threadId)
	{
		var name = node.Name;
		if (string.IsNullOrWhiteSpace(name) || (_threadNames.TryGetValue(threadId, out var named) && named.Name == name))
			return;

		_threadNames[threadId] = new NamedThread(node, profile, name);
		_ = SetThreadNameQuietlyAsync(host, threadId, name);
	}

	private async Task SetThreadNameQuietlyAsync(CodexAppServerHost host, string threadId, string name)
	{
		try
		{
			await host.SetThreadNameAsync(threadId, name).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			_threadNames.TryRemove(threadId, out _);
			_log.Warning(ex, "Naming Codex thread {ThreadId} failed", threadId);
		}
	}

	/// <summary>A rename in Tessyn (FR.33.7) is sent to the threads this engine named, when their host runs; otherwise after the next turn.</summary>
	private void WatchRenames()
	{
		if (_registry is null || Interlocked.Exchange(ref _renameWatch, 1) == 1)
			return;

		_registry.TreeChanged += OnTreeChanged;
	}

	private void OnTreeChanged(object? sender, TreeChangedEventArgs e)
	{
		if (e.Kind != TreeChangeKind.SettingsModified)
			return;

		foreach (var (threadId, named) in _threadNames)
		{
			var node = named.Node;
			if (node.CodexThreadId != threadId || node.Name == named.Name)
				continue;

			var host = _pool.Hosts.FirstOrDefault(h => string.Equals(h.ProfileId, named.Profile.ProfileId, StringComparison.OrdinalIgnoreCase));
			if (host is { State: CodexHostState.Running })
				SyncThreadName(host, named.Profile, node, threadId);
		}
	}

	private void EndWithError(IEngineEventSink sink, string text) =>
		sink.TurnEnded(new EngineTurnEnd(false, text, false, false, null, 0, 0, 0, _time.GetUtcNow()));

	/// <summary>The SYSTEM text of a turn whose host was stopped on purpose (FR.33.5 profile delete, Terminate all).</summary>
	private string StoppedText(CodexHostStopReason reason) =>
		reason == CodexHostStopReason.ProfileRemoved ? Constants.Codex.Texts.ProfileRemovedStop : Constants.Codex.Texts.HostStoppedOnPurpose;

	/// <summary>
	/// The input of <c>turn/start</c> (FR.33.8): the text, and a <c>localImage</c> item for each image attachment of the session (FR.31) whose path is
	/// in the text, when the model takes images. The path stays in the text.
	/// </summary>
	private List<CodexUserInput> BuildInput(SessionNodeModel node, string text, TurnSettings settings)
	{
		var input = new List<CodexUserInput> { CodexUserInput.FromText(text) };
		if (_attachments is null || string.IsNullOrEmpty(node.NodeId))
			return input;

		var modalities = _models?.Find(ModelOf(settings))?.InputModalities ?? Constants.Codex.Catalog.DefaultInputModalities;
		if (!modalities.Contains(Constants.Codex.Values.ImageModality, StringComparer.OrdinalIgnoreCase))
			return input;

		try
		{
			foreach (var attachment in _attachments.List(node.NodeId).Attachments)
			{
				if (attachment.Mime.StartsWith(Constants.Codex.Values.ImageMimePrefix, StringComparison.OrdinalIgnoreCase)
				    && text.Contains(attachment.Path, StringComparison.OrdinalIgnoreCase))
					input.Add(CodexUserInput.FromLocalImage(attachment.Path));
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TdcpException or TdcpInvalidParamsException)
		{
			_log.Warning(ex, "The attachments of Codex session {NodeId} could not be listed; the turn is sent without images", node.NodeId);
		}

		return input;
	}

	private string HostStoppedText(CodexHostCrashInfo crash) =>
		string.Format(Constants.Codex.Texts.HostStoppedFormat, crash.ExitCode?.ToString() ?? Constants.Codex.Texts.UnknownExitCode);

	/// <summary>The model sent on <c>thread/start</c> and every <c>turn/start</c> (FR.33.8); null for Default, which lets Codex choose.</summary>
	private string? ModelOf(TurnSettings? settings) => IsDefault(settings?.ModelId) ? null : settings!.ModelId;

	/// <summary>
	/// The effort of the turn; null for Default, which sends no effort (FR.33.3, FR.33.8). An effort the GPT model does not offer is sent as none
	/// (it stays stored, so it returns when the model changes back).
	/// </summary>
	private string? EffortOf(TurnSettings? settings)
	{
		var effort = IsDefault(settings?.Effort) ? null : settings!.Effort;
		var model  = ModelOf(settings);
		return effort is not null && _models?.IsGpt(model) == true ? _models.EffectiveEffort(model, effort) : effort;
	}

	private bool IsDefault(string? value) =>
		string.IsNullOrWhiteSpace(value) || string.Equals(value, Constants.Turns.DefaultOptionText, StringComparison.OrdinalIgnoreCase);

	private string ToolName(CodexToolKind kind) => kind switch
	{
		CodexToolKind.Command    => Constants.Codex.Protocol.ItemCommandExecution,
		CodexToolKind.FileChange => Constants.Codex.Protocol.ItemFileChange,
		CodexToolKind.McpTool    => Constants.Codex.Protocol.ItemMcpToolCall,
		CodexToolKind.WebSearch  => Constants.Codex.Protocol.ItemWebSearch,
		_                        => Constants.Codex.Protocol.ItemImageView,
	};

	private int ToInt(long value) => value > int.MaxValue ? int.MaxValue : (int)value;
}
