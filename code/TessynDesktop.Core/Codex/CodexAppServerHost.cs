using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using TessynDesktop.Codex.Protocol;

namespace TessynDesktop.Codex;

/// <summary>
/// One <c>codex app-server</c> process for one Codex profile (<c>CODEX_HOME</c>), shared by every Codex session on that profile (FR.33.6).
/// <list type="bullet">
/// <item><b>Lifetime.</b> Started on the first call; stopped after <see cref="CodexHostOptions.IdleShutdown"/> with no call in flight, no turn
/// in progress and no thread loaded (measured on the injected <see cref="TimeProvider"/>), by <see cref="StopAsync"/> and by
/// <see cref="TerminateAsync"/>.</item>
/// <item><b>Handshake.</b> <c>initialize</c> with <c>clientInfo.name</c> <c>tessyn_desktop</c> and <c>experimentalApi: false</c>, then <c>initialized</c>.</item>
/// <item><b>Crash.</b> A process that ends without being asked to fails every waiting call with <see cref="CodexHostCrashedException"/>
/// (exit code and the last stderr lines), raises <see cref="Crashed"/>, and the next call starts a new process (a new <see cref="Generation"/>,
/// on which every thread must be resumed again).</item>
/// <item><b>Server requests</b> are all answered by <see cref="CodexServerRequestResponder"/> (FR.33.8).</item>
/// <item><b>Thread failures</b> of <c>thread/resume</c> and <c>turn/start</c> are raised as <see cref="CodexThreadException"/> (FR.33.7).</item>
/// </list>
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class CodexAppServerHost : IAsyncDisposable
{
	private static readonly ILogger _log = Log.ForContext<CodexAppServerHost>();

	private const int MaxModelPages = 100;

	private readonly CodexHostOptions            _options;
	private readonly ICodexProcessLauncher       _launcher;
	private readonly TimeProvider                _time;
	private readonly CodexServerRequestResponder _responder;
	private readonly CodexThreadErrorClassifier  _classifier    = new();
	private readonly SemaphoreSlim               _gate          = new(1, 1);
	private readonly object                      _stateLock     = new();
	private readonly HashSet<string>             _loadedThreads = new(StringComparer.Ordinal);
	private readonly HashSet<string>             _activeTurns   = new(StringComparer.Ordinal);
	private HostSession?                         _session;
	private CodexHostState                       _state;
	private ITimer?                              _idleTimer;
	private object?                              _idleToken;
	private int                                  _inFlight;
	private int                                  _generation;
	private bool                                 _disposed;

	public CodexAppServerHost(CodexHostOptions options, ICodexProcessLauncher launcher, TimeProvider? time = null)
	{
		_options   = options;
		_launcher  = launcher;
		_time      = time ?? TimeProvider.System;
		_responder = new CodexServerRequestResponder(options.ProfileId);
	}

	public string ProfileId => _options.ProfileId;

	public string? CodexHome => _options.CodexHome;

	public CodexHostState State
	{
		get { lock (_stateLock) return _state; }
	}

	/// <summary>Counts launched processes; 0 before the first. A thread loaded on an older generation must be resumed again (FR.33.7).</summary>
	public int Generation => Volatile.Read(ref _generation);

	/// <summary>The process id of the running app-server, else null.</summary>
	public int? ProcessId
	{
		get { lock (_stateLock) return _session?.Process.Id; }
	}

	/// <summary>The <c>initialize</c> result of the current process.</summary>
	public CodexInitializeResult? ServerInfo { get; private set; }

	public CodexHostCrashInfo? LastCrash { get; private set; }

	/// <summary>True while the idle shutdown timer runs.</summary>
	public bool IsIdleShutdownArmed
	{
		get { lock (_stateLock) return _idleTimer is not null; }
	}

	/// <summary>Every notification of the current process, in stream order, on the read loop (handlers must be quick).</summary>
	public event Action<CodexNotification>? NotificationReceived;

	/// <summary>The process ended without being asked to (FR.33.6).</summary>
	public event Action<CodexHostCrashInfo>? Crashed;

	/// <summary>
	/// The process is being stopped on purpose (a profile delete, Terminate all, dispose, the idle shutdown). Raised before the process is closed,
	/// so turns still waiting on it end at once with an error instead of waiting for a <c>turn/completed</c> that never comes.
	/// </summary>
	public event Action<CodexHostStopReason>? Stopped;

	/// <summary>True when the thread was started or resumed on the current process and not unsubscribed or closed since.</summary>
	public bool IsThreadLoaded(string threadId)
	{
		lock (_stateLock)
			return _loadedThreads.Contains(threadId);
	}

	/// <summary>Starts the app-server and does the handshake, unless it already runs.</summary>
	public async Task EnsureStartedAsync(CancellationToken cancellationToken = default)
	{
		Interlocked.Increment(ref _inFlight);
		EvaluateIdle();
		try
		{
			await GetSessionAsync(cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			Interlocked.Decrement(ref _inFlight);
			EvaluateIdle();
		}
	}

	public Task<CodexGetAccountResult> ReadAccountAsync(bool refreshToken = false, CancellationToken cancellationToken = default) =>
		InvokeAsync<CodexGetAccountResult>(Constants.Codex.Methods.AccountRead, new CodexGetAccountParams { RefreshToken = refreshToken ? true : null }, cancellationToken);

	public Task<CodexRateLimitsResult> ReadRateLimitsAsync(CancellationToken cancellationToken = default) =>
		InvokeAsync<CodexRateLimitsResult>(Constants.Codex.Methods.AccountRateLimitsRead, null, cancellationToken);

	/// <summary>Every page of <c>model/list</c> (FR.33.2).</summary>
	public async Task<IReadOnlyList<CodexModel>> ListModelsAsync(bool includeHidden = false, CancellationToken cancellationToken = default)
	{
		var models = new List<CodexModel>();
		string? cursor = null;
		for (var page = 0; page < MaxModelPages; page++)
		{
			var result = await InvokeAsync<CodexModelListResult>(
				Constants.Codex.Methods.ModelList,
				new CodexModelListParams { Cursor = cursor, IncludeHidden = includeHidden },
				cancellationToken).ConfigureAwait(false);

			models.AddRange(result.Data);
			if (string.IsNullOrEmpty(result.NextCursor) || result.NextCursor == cursor)
				return models;
			cursor = result.NextCursor;
		}

		_log.Warning("Codex ({Profile}) model/list returned more than {Pages} pages; the list is cut there", ProfileId, MaxModelPages);
		return models;
	}

	/// <summary><c>thread/start</c> (FR.33.7). The thread counts as loaded on this process from here on.</summary>
	public async Task<CodexThreadStartResult> StartThreadAsync(CodexThreadStartParams parameters, CancellationToken cancellationToken = default)
	{
		var result = await InvokeAsync<CodexThreadStartResult>(Constants.Codex.Methods.ThreadStart, parameters, cancellationToken).ConfigureAwait(false);
		MarkLoaded(result.Thread.Id, loaded: true);
		return result;
	}

	/// <summary><c>thread/resume</c> (FR.33.7).</summary>
	/// <exception cref="CodexThreadException">The thread is gone, or another Codex process has it open.</exception>
	public async Task<CodexThreadResumeResult> ResumeThreadAsync(CodexThreadResumeParams parameters, CancellationToken cancellationToken = default)
	{
		try
		{
			var result = await InvokeAsync<CodexThreadResumeResult>(Constants.Codex.Methods.ThreadResume, parameters, cancellationToken).ConfigureAwait(false);
			MarkLoaded(result.Thread.Id, loaded: true);
			return result;
		}
		catch (CodexRpcException ex) when (ex is not CodexThreadException && _classifier.Classify(ex) != CodexThreadErrorKind.Unknown)
		{
			throw new CodexThreadException(_classifier.Classify(ex), parameters.ThreadId, ex);
		}
	}

	public Task SetThreadNameAsync(string threadId, string name, CancellationToken cancellationToken = default) =>
		InvokeAsync<CodexEmptyResult>(Constants.Codex.Methods.ThreadNameSet, new CodexThreadSetNameParams { ThreadId = threadId, Name = name }, cancellationToken);

	/// <summary><c>thread/unsubscribe</c>. The thread no longer counts as loaded on this process.</summary>
	public async Task<string?> UnsubscribeThreadAsync(string threadId, CancellationToken cancellationToken = default)
	{
		var result = await InvokeAsync<CodexThreadUnsubscribeResult>(
			Constants.Codex.Methods.ThreadUnsubscribe, new CodexThreadUnsubscribeParams { ThreadId = threadId }, cancellationToken).ConfigureAwait(false);
		MarkLoaded(threadId, loaded: false);
		return result.Status;
	}

	/// <summary><c>turn/start</c> (FR.33.8). It answers at once; the turn runs on as notifications.</summary>
	/// <exception cref="CodexThreadException">With <see cref="CodexThreadErrorKind.NotLoaded"/>: resume the thread once and retry.</exception>
	public async Task<CodexTurnStartResult> StartTurnAsync(CodexTurnStartParams parameters, CancellationToken cancellationToken = default)
	{
		try
		{
			return await InvokeAsync<CodexTurnStartResult>(Constants.Codex.Methods.TurnStart, parameters, cancellationToken).ConfigureAwait(false);
		}
		catch (CodexRpcException ex) when (ex is not CodexThreadException && _classifier.Classify(ex) != CodexThreadErrorKind.Unknown)
		{
			var kind = _classifier.Classify(ex);
			if (kind == CodexThreadErrorKind.NotLoaded)
				MarkLoaded(parameters.ThreadId, loaded: false);
			throw new CodexThreadException(kind, parameters.ThreadId, ex);
		}
	}

	/// <summary><c>turn/interrupt</c> (FR.33.8). The turn ends when <c>turn/completed</c> with <c>interrupted</c> arrives.</summary>
	public Task InterruptTurnAsync(string threadId, string turnId, CancellationToken cancellationToken = default) =>
		InvokeAsync<CodexEmptyResult>(Constants.Codex.Methods.TurnInterrupt, new CodexTurnInterruptParams { ThreadId = threadId, TurnId = turnId }, cancellationToken);

	/// <summary><c>thread/compact/start</c> (FR.33.12). Progress comes as a turn of its own with a <c>contextCompaction</c> item.</summary>
	public Task CompactThreadAsync(string threadId, CancellationToken cancellationToken = default) =>
		InvokeAsync<CodexEmptyResult>(Constants.Codex.Methods.ThreadCompactStart, new CodexThreadCompactStartParams { ThreadId = threadId }, cancellationToken);

	/// <summary><c>account/login/start</c> with <c>chatgpt</c> (an <c>authUrl</c> to open) or <c>chatgptDeviceCode</c> (a code and URL) (FR.33.5).</summary>
	public Task<CodexLoginStartResult> LoginStartAsync(bool useDeviceCode = false, CancellationToken cancellationToken = default) =>
		InvokeAsync<CodexLoginStartResult>(
			Constants.Codex.Methods.AccountLoginStart,
			new CodexLoginStartParams { Type = useDeviceCode ? Constants.Codex.Values.LoginTypeChatGptDeviceCode : Constants.Codex.Values.LoginTypeChatGpt },
			cancellationToken);

	public async Task<string?> CancelLoginAsync(string loginId, CancellationToken cancellationToken = default)
	{
		var result = await InvokeAsync<CodexLoginCancelResult>(
			Constants.Codex.Methods.AccountLoginCancel, new CodexLoginCancelParams { LoginId = loginId }, cancellationToken).ConfigureAwait(false);
		return result.Status;
	}

	public Task LogoutAsync(CancellationToken cancellationToken = default) =>
		InvokeAsync<CodexEmptyResult>(Constants.Codex.Methods.AccountLogout, null, cancellationToken);

	/// <summary>Closes the app-server's stdin and waits up to <see cref="CodexHostOptions.StopGrace"/> for it to exit, then kills it.</summary>
	public Task StopAsync(CodexHostStopReason reason = CodexHostStopReason.Requested) => StopCoreAsync(onlyIfIdle: false, kill: false, reason);

	/// <summary>Kills the app-server at once ("Terminate all", FR.15.5).</summary>
	public Task TerminateAsync() => StopCoreAsync(onlyIfIdle: false, kill: true, CodexHostStopReason.Terminated);

	public async ValueTask DisposeAsync()
	{
		_disposed = true;
		await StopAsync().ConfigureAwait(false);
	}

	private async Task<TResult> InvokeAsync<TResult>(string method, object? parameters, CancellationToken cancellationToken)
	{
		Interlocked.Increment(ref _inFlight);
		EvaluateIdle();
		try
		{
			var session = await GetSessionAsync(cancellationToken).ConfigureAwait(false);

			using var timeout = new CancellationTokenSource(_options.RequestTimeout, _time);
			using var linked  = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
			try
			{
				return await session.Connection.SendRequestAsync<TResult>(method, parameters, linked.Token).ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
			{
				_log.Warning("Codex ({Profile}) did not answer {Method} within {Timeout}", ProfileId, method, _options.RequestTimeout);
				throw new TimeoutException($"Codex ({ProfileId}) did not answer {method} within {_options.RequestTimeout}.");
			}
			catch (CodexConnectionLostException lost)
			{
				var crash = await session.Ended.ConfigureAwait(false);
				if (crash is null)
					throw;
				throw new CodexHostCrashedException(crash, lost);
			}
		}
		finally
		{
			Interlocked.Decrement(ref _inFlight);
			EvaluateIdle();
		}
	}

	private async Task<HostSession> GetSessionAsync(CancellationToken cancellationToken)
	{
		lock (_stateLock)
		{
			if (_session is not null && _state == CodexHostState.Running)
				return _session;
		}

		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			lock (_stateLock)
			{
				if (_session is not null && _state == CodexHostState.Running)
					return _session;
			}

			ObjectDisposedException.ThrowIf(_disposed, this);
			return await StartSessionAsync(cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			_gate.Release();
		}
	}

	private async Task<HostSession> StartSessionAsync(CancellationToken cancellationToken)
	{
		SetState(CodexHostState.Starting);

		ICodexProcess process;
		try
		{
			process = _launcher.Launch(BuildLaunchSpec());
		}
		catch
		{
			SetState(CodexHostState.Stopped);
			throw;
		}

		var generation = Interlocked.Increment(ref _generation);
		var connection = new CodexJsonRpcConnection(process.StandardOutput, process.StandardInput);
		var session    = new HostSession(process, connection, generation);

		connection.NotificationReceived += n => OnNotification(session, n);
		connection.ServerRequestHandler  = _responder.HandleAsync;
		session.StderrPump               = Task.Run(() => PumpStderrAsync(session));
		connection.Start();
		_ = connection.Closed.ContinueWith(_ => OnConnectionClosedAsync(session), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();

		try
		{
			using var timeout = new CancellationTokenSource(_options.RequestTimeout, _time);
			using var linked  = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

			var initialize = new CodexInitializeParams
			{
				ClientInfo   = new CodexClientInfo { Name = Constants.Codex.Client.Name, Title = Constants.Codex.Client.Title, Version = _options.ClientVersion },
				Capabilities = new CodexInitializeCapabilities { ExperimentalApi = false },
			};
			ServerInfo = await connection.SendRequestAsync<CodexInitializeResult>(Constants.Codex.Methods.Initialize, initialize, linked.Token).ConfigureAwait(false);
			await connection.SendNotificationAsync(Constants.Codex.Methods.Initialized, null, linked.Token).ConfigureAwait(false);
		}
		catch (CodexConnectionLostException lost)
		{
			SetState(CodexHostState.Stopped);
			var crash = await session.Ended.ConfigureAwait(false);
			throw crash is null ? lost : new CodexHostCrashedException(crash, lost);
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "Codex ({Profile}) handshake failed; the process is stopped", ProfileId);
			session.StopRequested = true;
			process.Kill();
			await connection.DisposeAsync().ConfigureAwait(false);
			SetState(CodexHostState.Stopped);
			if (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
				throw new TimeoutException($"Codex ({ProfileId}) did not answer initialize within {_options.RequestTimeout}.", ex);
			throw;
		}

		lock (_stateLock)
		{
			_session = session;
			_state   = CodexHostState.Running;
		}

		_log.Information("Codex ({Profile}) app-server {Pid} is ready (generation {Generation}, {UserAgent})",
			ProfileId, process.Id, generation, ServerInfo?.UserAgent);
		return session;
	}

	private CodexLaunchSpec BuildLaunchSpec()
	{
		var arguments = new List<string> { Constants.Codex.Launch.AppServerArgument };
		if (!string.IsNullOrEmpty(_options.CodexHome))
		{
			arguments.Add(Constants.Codex.Launch.ConfigOverrideArgument);
			arguments.Add(Constants.Codex.Launch.FileCredentialStoreOverride);
		}
		foreach (var configOverride in _options.ConfigOverrides)
		{
			arguments.Add(Constants.Codex.Launch.ConfigOverrideArgument);
			arguments.Add(configOverride);
		}

		return new CodexLaunchSpec(_options.ExecutablePath, arguments, _options.CodexHome, ResolveWorkingDirectory());
	}

	private string ResolveWorkingDirectory()
	{
		if (!string.IsNullOrEmpty(_options.WorkingDirectory))
			return _options.WorkingDirectory;
		if (!string.IsNullOrEmpty(_options.CodexHome))
			return _options.CodexHome;

		var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		var defaultHome = Path.Combine(userProfile, Constants.Codex.Launch.DefaultHomeFolderName);
		return Directory.Exists(defaultHome) ? defaultHome : userProfile;
	}

	private async Task PumpStderrAsync(HostSession session)
	{
		try
		{
			string? line;
			while ((line = await session.Process.StandardError.ReadLineAsync().ConfigureAwait(false)) is not null)
			{
				session.AddStderrLine(line);
				_log.Debug("Codex ({Profile}) stderr: {Line}", ProfileId, line);
			}
		}
		catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
		{
			// The process is gone; the tail kept so far is what there is.
		}
	}

	private async Task OnConnectionClosedAsync(HostSession session)
	{
		if (session.StopRequested)
		{
			session.End(null);
			return;
		}

		int? exitCode = null;
		try
		{
			using var wait = new CancellationTokenSource(Constants.Codex.CrashReportWait, _time);
			await session.Process.WaitForExitAsync(wait.Token).ConfigureAwait(false);
			exitCode = session.Process.ExitCode;
		}
		catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException)
		{
			exitCode = session.Process.ExitCode;
		}

		await Task.WhenAny(session.StderrPump, Task.Delay(Constants.Codex.CrashReportWait, _time)).ConfigureAwait(false);

		var crash = new CodexHostCrashInfo(ProfileId, exitCode, session.StderrTail());
		_log.Warning("Codex ({Profile}) app-server {Pid} stopped unexpectedly (exit {ExitCode}). Last stderr lines:{NewLine}{StderrTail}",
			ProfileId, session.Process.Id, exitCode?.ToString() ?? "unknown", Environment.NewLine, string.Join(Environment.NewLine, crash.StderrTail));

		lock (_stateLock)
		{
			if (ReferenceEquals(_session, session))
			{
				_session = null;
				_state   = CodexHostState.Stopped;
				_loadedThreads.Clear();
				_activeTurns.Clear();
				DisarmIdleLocked();
			}
			LastCrash = crash;
		}

		session.End(crash);
		session.Process.Kill();
		session.Process.Dispose();

		try
		{
			Crashed?.Invoke(crash);
		}
		catch (Exception ex)
		{
			_log.Error(ex, "A Codex crash handler failed");
		}
	}

	private async Task StopCoreAsync(bool onlyIfIdle, bool kill, CodexHostStopReason reason)
	{
		await _gate.WaitAsync().ConfigureAwait(false);
		try
		{
			HostSession? session;
			lock (_stateLock)
			{
				if (onlyIfIdle && !IsIdleLocked())
					return;

				session  = _session;
				_session = null;
				_state   = CodexHostState.Stopped;
				_loadedThreads.Clear();
				_activeTurns.Clear();
				DisarmIdleLocked();
			}

			if (session is null)
				return;

			session.StopRequested = true;
			try
			{
				Stopped?.Invoke(reason);
			}
			catch (Exception ex)
			{
				_log.Error(ex, "A Codex host-stopped handler failed");
			}

			if (kill)
				session.Process.Kill();
			else
				await CloseGracefullyAsync(session).ConfigureAwait(false);

			await session.Connection.DisposeAsync().ConfigureAwait(false);
			session.Process.Dispose();
			_log.Information("Codex ({Profile}) app-server stopped (generation {Generation}{How})", ProfileId, session.Generation, kill ? ", killed" : string.Empty);
		}
		finally
		{
			_gate.Release();
		}
	}

	private async Task CloseGracefullyAsync(HostSession session)
	{
		try
		{
			session.Process.CloseStandardInput();
		}
		catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
		{
		}

		try
		{
			using var grace = new CancellationTokenSource(_options.StopGrace, _time);
			await session.Process.WaitForExitAsync(grace.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			_log.Warning("Codex ({Profile}) app-server did not exit within {Grace} of closing its input; killing it", ProfileId, _options.StopGrace);
			session.Process.Kill();
		}
	}

	private void OnNotification(HostSession session, CodexNotification notification)
	{
		TrackActivity(session, notification);

		try
		{
			NotificationReceived?.Invoke(notification);
		}
		catch (Exception ex)
		{
			_log.Error(ex, "A handler of Codex notification {Method} failed", notification.Method);
		}
	}

	private void TrackActivity(HostSession session, CodexNotification notification)
	{
		var method = notification.Method;
		if (method is not (Constants.Codex.Methods.TurnStarted or Constants.Codex.Methods.TurnCompleted or Constants.Codex.Methods.ThreadClosed))
			return;

		lock (_stateLock)
		{
			if (!ReferenceEquals(_session, session))
				return;

			if (method == Constants.Codex.Methods.ThreadClosed)
			{
				if (TryGetString(notification.Params, Constants.Codex.Rpc.ThreadId) is { } closedThread)
					_loadedThreads.Remove(closedThread);
			}
			else if (notification.Params.TryGetProperty(Constants.Codex.Rpc.Turn, out var turn) && TryGetString(turn, Constants.Codex.Rpc.Id) is { } turnId)
			{
				if (method == Constants.Codex.Methods.TurnStarted)
					_activeTurns.Add(turnId);
				else
					_activeTurns.Remove(turnId);
			}
		}

		EvaluateIdle();
	}

	private void MarkLoaded(string threadId, bool loaded)
	{
		lock (_stateLock)
		{
			if (loaded)
				_loadedThreads.Add(threadId);
			else
				_loadedThreads.Remove(threadId);
		}

		EvaluateIdle();
	}

	private void EvaluateIdle()
	{
		lock (_stateLock)
		{
			if (!IsIdleLocked())
			{
				DisarmIdleLocked();
				return;
			}

			if (_idleTimer is not null)
				return;

			var token  = new object();
			_idleToken = token;
			_idleTimer = _time.CreateTimer(OnIdleTimer, token, _options.IdleShutdown, Timeout.InfiniteTimeSpan);
		}
	}

	private bool IsIdleLocked() =>
		_session is not null
		&& _state == CodexHostState.Running
		&& Volatile.Read(ref _inFlight) == 0
		&& _loadedThreads.Count == 0
		&& _activeTurns.Count == 0;

	private void DisarmIdleLocked()
	{
		_idleTimer?.Dispose();
		_idleTimer = null;
		_idleToken = null;
	}

	private void OnIdleTimer(object? token)
	{
		lock (_stateLock)
		{
			if (!ReferenceEquals(token, _idleToken))
				return;
			DisarmIdleLocked();
		}

		_ = StopForIdleAsync();
	}

	private async Task StopForIdleAsync()
	{
		try
		{
			_log.Information("Codex ({Profile}) app-server idle for {Idle}; stopping it", ProfileId, _options.IdleShutdown);
			await StopCoreAsync(onlyIfIdle: true, kill: false, CodexHostStopReason.Idle).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "Codex ({Profile}) idle shutdown failed", ProfileId);
		}
	}

	private void SetState(CodexHostState state)
	{
		lock (_stateLock)
			_state = state;
	}

	private static string? TryGetString(JsonElement element, string property) =>
		element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;
}
