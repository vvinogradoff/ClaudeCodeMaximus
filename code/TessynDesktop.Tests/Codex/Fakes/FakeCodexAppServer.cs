using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Codex;

namespace TessynDesktop.Tests.Codex.Fakes;

/// <summary>
/// The in-process fake <c>codex app-server</c> of FR.33.19. It reads Tessyn's lines from the fake process's stdin and answers on its
/// stdout with its own minimal JSON-RPC code (independent of <see cref="CodexJsonRpcConnection"/>), speaking the stable subset Tessyn uses:
/// the handshake, <c>account/*</c>, <c>model/list</c>, <c>thread/*</c> and <c>turn/*</c>. After answering <c>turn/start</c> (and
/// <c>thread/compact/start</c>) it plays a scripted list of <see cref="FakeCodexStep"/>s: notifications, server requests (whose answers
/// it records), a crash or a hang. Any handler can be replaced with <see cref="On"/>. Closing its stdin makes it exit with code 0.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class FakeCodexAppServer
{
	private readonly SemaphoreSlim                                          _writeLock      = new(1, 1);
	private readonly ConcurrentDictionary<string, Func<FakeCodexCall, Task>> _handlers       = new();
	private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _serverCalls = new();
	private readonly ConcurrentDictionary<string, TurnPlayback>             _playbacks      = new();
	private readonly ConcurrentDictionary<string, byte>                     _loadedThreads  = new();
	private long _nextServerCallId = 900;
	private int  _threadCounter;
	private int  _turnCounter;

	public FakeCodexAppServer(CodexLaunchSpec spec, int processId, FakeCodexScripts scripts)
	{
		Spec    = spec;
		Scripts = scripts;
		Process = new FakeCodexProcess(processId);
		ModelPages = scripts.DefaultModelPages();
		TurnScript = start => Scripts.TextTurn(start.ThreadId, start.TurnId, "Hello from the fake.");
	}

	public CodexLaunchSpec   Spec    { get; }
	public FakeCodexProcess  Process { get; }
	public FakeCodexScripts  Scripts { get; }

	/// <summary>Every message Tessyn sent, in order.</summary>
	public ConcurrentQueue<FakeCodexMessage> Received { get; } = new();

	/// <summary>Tessyn's answers to the server requests of the scripts, in order (each the whole response message).</summary>
	public ConcurrentQueue<JsonElement> ServerRequestAnswers { get; } = new();

	public bool SignedIn { get; set; } = true;

	public List<JsonArray> ModelPages { get; set; }

	/// <summary>Threads whose resume fails with <c>no rollout found for thread id …</c>.</summary>
	public ConcurrentDictionary<string, byte> GoneThreads { get; } = new();

	/// <summary>Threads whose resume fails with <c>… already has an active writer</c>.</summary>
	public ConcurrentDictionary<string, byte> ActiveWriterThreads { get; } = new();

	/// <summary>Extra members added to the <c>thread/resume</c> result (for example a very large one).</summary>
	public Action<JsonObject>? DecorateResumeResult { get; set; }

	/// <summary>Builds the steps played after <c>turn/start</c>; default: one short answer.</summary>
	public Func<FakeTurnStart, List<FakeCodexStep>> TurnScript { get; set; }

	public bool IsThreadLoaded(string threadId) => _loadedThreads.ContainsKey(threadId);

	public void MarkLoaded(string threadId) => _loadedThreads[threadId] = 0;

	/// <summary>Forgets a loaded thread behind the client's back, so its next <c>turn/start</c> fails with <c>thread not found</c>.</summary>
	public void Unload(string threadId) => _loadedThreads.TryRemove(threadId, out _);

	public void On(string method, Func<FakeCodexCall, Task> handler) => _handlers[method] = handler;

	public void Start() => _ = Task.Run(ReadLoopAsync);

	public IReadOnlyList<FakeCodexMessage> RequestsOf(string method) => Received.Where(m => m.Method == method && m.Id is not null).ToList();

	public IReadOnlyList<FakeCodexMessage> NotificationsOf(string method) => Received.Where(m => m.Method == method && m.Id is null).ToList();

	public async Task<FakeCodexMessage> WaitForAsync(string method, int count = 1, TimeSpan? timeout = null)
	{
		var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
		while (DateTime.UtcNow < deadline)
		{
			var matches = Received.Where(m => m.Method == method).ToList();
			if (matches.Count >= count)
				return matches[count - 1];
			await Task.Delay(10).ConfigureAwait(false);
		}
		throw new TimeoutException($"The fake app-server did not receive {method} x{count}.");
	}

	public async Task WriteLineAsync(string json)
	{
		var bytes = Encoding.UTF8.GetBytes(json + "\n");
		await _writeLock.WaitAsync().ConfigureAwait(false);
		try
		{
			await Process.StdoutPipe.WriteAsync(bytes).ConfigureAwait(false);
		}
		finally
		{
			_writeLock.Release();
		}
	}

	public Task NotifyAsync(string method, JsonObject parameters) =>
		WriteLineAsync(new JsonObject { ["method"] = method, ["params"] = parameters, ["emittedAtMs"] = 1795332667679 }.ToJsonString());

	/// <summary>Sends a server request and returns Tessyn's whole response message.</summary>
	public async Task<JsonElement> RequestAsync(string method, JsonObject parameters)
	{
		var id = Interlocked.Increment(ref _nextServerCallId);
		var answer = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
		_serverCalls[id] = answer;
		await WriteLineAsync(new JsonObject { ["method"] = method, ["id"] = id, ["params"] = parameters }.ToJsonString()).ConfigureAwait(false);
		var response = await answer.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
		ServerRequestAnswers.Enqueue(response);
		return response;
	}

	/// <summary>The process dies: stderr lines, the exit code, then the end of stdout.</summary>
	public void Crash(int exitCode, params string[] stderr)
	{
		foreach (var line in stderr)
			Process.StderrPipe.Write(Encoding.UTF8.GetBytes(line + "\n"));
		Process.Exit(exitCode);
	}

	public string NewThreadId() => $"01a20229-0000-7000-8000-{Interlocked.Increment(ref _threadCounter):D12}";

	public string NewTurnId() => $"01a2022a-0000-7000-9000-{Interlocked.Increment(ref _turnCounter):D12}";

	private async Task ReadLoopAsync()
	{
		using var reader = new StreamReader(Process.StdinPipe, new UTF8Encoding(false));
		try
		{
			string? line;
			while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) is not null)
			{
				if (line.Length == 0)
					continue;
				await HandleLineAsync(line).ConfigureAwait(false);
			}
		}
		catch (IOException)
		{
		}

		// The app-server exits when its stdin closes (spike: every host exited when its stdin closed).
		foreach (var playback in _playbacks.Values)
			playback.Cancel.Cancel();
		Process.Exit(0);
	}

	private async Task HandleLineAsync(string line)
	{
		JsonElement message;
		using (var document = JsonDocument.Parse(line))
			message = document.RootElement.Clone();

		string? method = message.TryGetProperty("method", out var m) ? m.GetString() : null;
		string? rawId  = message.TryGetProperty("id", out var id) ? id.GetRawText() : null;
		var parameters = message.TryGetProperty("params", out var p) ? p : default;
		Received.Enqueue(new FakeCodexMessage(method, rawId, parameters, message));

		if (method is null)
		{
			if (rawId is not null && long.TryParse(rawId, out var answered) && _serverCalls.TryRemove(answered, out var waiter))
				waiter.TrySetResult(message);
			return;
		}

		if (rawId is null)
			return;

		var call = new FakeCodexCall(this, rawId, method, parameters);
		var handler = _handlers.TryGetValue(method, out var custom) ? custom : DefaultHandleAsync;
		_ = Task.Run(async () =>
		{
			try
			{
				await handler(call).ConfigureAwait(false);
			}
			catch (IOException)
			{
				// The fake process died while answering.
			}
		});
	}

	private Task DefaultHandleAsync(FakeCodexCall call) => call.Method switch
	{
		"initialize"               => call.ReplyAsync(Scripts.InitializeResult(Spec.CodexHome ?? @"C:\Users\u\.codex")),
		"account/read"             => call.ReplyAsync(SignedIn ? Scripts.SignedInAccount() : Scripts.SignedOutAccount()),
		"account/rateLimits/read"  => SignedIn ? call.ReplyAsync(Scripts.RateLimitsResult()) : call.ErrorAsync(-32600, "codex account authentication required to read rate limits"),
		"model/list"               => ListModelsAsync(call),
		"account/login/start"      => LoginStartAsync(call),
		"account/login/cancel"     => call.ReplyAsync(new JsonObject { ["status"] = "canceled" }),
		"account/logout"           => LogoutAsync(call),
		"thread/start"             => StartThreadAsync(call),
		"thread/resume"            => ResumeThreadAsync(call),
		"thread/name/set"          => SetNameAsync(call),
		"thread/unsubscribe"       => UnsubscribeAsync(call),
		"thread/compact/start"     => CompactAsync(call),
		"turn/start"               => StartTurnAsync(call),
		"turn/interrupt"           => InterruptAsync(call),
		_                          => call.ErrorAsync(-32601, $"unknown method {call.Method}"),
	};

	private Task ListModelsAsync(FakeCodexCall call)
	{
		var cursor = call.ParamString("cursor");
		var index  = cursor is null ? 0 : int.Parse(cursor.Replace("page-", string.Empty));
		var next   = index + 1 < ModelPages.Count ? $"page-{index + 1}" : null;
		return call.ReplyAsync(new JsonObject { ["data"] = ModelPages[index].DeepClone(), ["nextCursor"] = next });
	}

	private Task LoginStartAsync(FakeCodexCall call) => call.ParamString("type") == "chatgptDeviceCode"
		? call.ReplyAsync(new JsonObject { ["type"] = "chatgptDeviceCode", ["loginId"] = "login-2", ["verificationUrl"] = "https://auth.openai.com/codex/device", ["userCode"] = "ABCD-1234" })
		: call.ReplyAsync(new JsonObject { ["type"] = "chatgpt", ["loginId"] = "login-1", ["authUrl"] = "https://auth.openai.com/oauth/authorize?fake=1" });

	private async Task LogoutAsync(FakeCodexCall call)
	{
		SignedIn = false;
		await call.ReplyEmptyAsync().ConfigureAwait(false);
		await NotifyAsync("account/updated", new JsonObject { ["authMode"] = null, ["planType"] = null, ["reason"] = null }).ConfigureAwait(false);
	}

	private async Task StartThreadAsync(FakeCodexCall call)
	{
		var threadId = NewThreadId();
		var cwd      = call.ParamString("cwd");
		MarkLoaded(threadId);
		await call.ReplyAsync(new JsonObject
		{
			["thread"]             = Scripts.Thread(threadId, cwd),
			["model"]              = call.ParamString("model") ?? "gpt-x-sol",
			["modelProvider"]      = "openai",
			["cwd"]                = cwd,
			["instructionSources"] = new JsonArray(),
			["approvalPolicy"]     = "never",
			["approvalsReviewer"]  = "user",
			["sandbox"]            = new JsonObject { ["type"] = "dangerFullAccess" },
			["reasoningEffort"]    = null,
		}).ConfigureAwait(false);
		await NotifyAsync("thread/started", new JsonObject { ["thread"] = Scripts.Thread(threadId, cwd) }).ConfigureAwait(false);
	}

	private Task ResumeThreadAsync(FakeCodexCall call)
	{
		var threadId = call.ParamString("threadId") ?? string.Empty;
		if (GoneThreads.ContainsKey(threadId))
			return call.ErrorAsync(-32600, $"no rollout found for thread id {threadId}");
		if (ActiveWriterThreads.ContainsKey(threadId))
			return call.ErrorAsync(-32600, $"thread {threadId} already has an active writer");

		MarkLoaded(threadId);
		var result = new JsonObject
		{
			["thread"]               = Scripts.Thread(threadId, @"C:\Proj\Alpha", "Session name", @"\\?\C:\Users\u\.codex\sessions\rollout.jsonl"),
			["model"]                = "gpt-x-sol",
			["modelProvider"]        = "openai",
			["cwd"]                  = @"C:\Proj\Alpha",
			["instructionSources"]   = new JsonArray(@"C:\Proj\Alpha\CLAUDE.md"),
			["approvalPolicy"]       = "never",
			["approvalsReviewer"]    = "user",
			["sandbox"]              = new JsonObject { ["type"] = "dangerFullAccess" },
			["reasoningEffort"]      = "low",
			["turnsBackwardsCursor"] = null,
			["itemsBackwardsCursor"] = null,
		};
		DecorateResumeResult?.Invoke(result);
		return call.ReplyAsync(result);
	}

	private async Task SetNameAsync(FakeCodexCall call)
	{
		await call.ReplyEmptyAsync().ConfigureAwait(false);
		await NotifyAsync("thread/name/updated", new JsonObject { ["threadId"] = call.ParamString("threadId"), ["threadName"] = call.ParamString("name") }).ConfigureAwait(false);
	}

	private Task UnsubscribeAsync(FakeCodexCall call)
	{
		var threadId = call.ParamString("threadId") ?? string.Empty;
		var wasLoaded = _loadedThreads.TryRemove(threadId, out _);
		return call.ReplyAsync(new JsonObject { ["status"] = wasLoaded ? "unsubscribed" : "notLoaded" });
	}

	private async Task CompactAsync(FakeCodexCall call)
	{
		var threadId = call.ParamString("threadId") ?? string.Empty;
		var turnId   = NewTurnId();
		await call.ReplyEmptyAsync().ConfigureAwait(false);
		Play(turnId, Scripts.CompactionTurn(threadId, turnId));
	}

	private async Task StartTurnAsync(FakeCodexCall call)
	{
		var threadId = call.ParamString("threadId") ?? string.Empty;
		if (!IsThreadLoaded(threadId))
		{
			await call.ErrorAsync(-32600, $"thread not found: {threadId}").ConfigureAwait(false);
			return;
		}

		var turnId = NewTurnId();
		var steps  = TurnScript(new FakeTurnStart(threadId, turnId, call.Params));
		await call.ReplyAsync(new JsonObject { ["turn"] = Scripts.Turn(turnId, "inProgress") }).ConfigureAwait(false);
		Play(turnId, steps);
	}

	private async Task InterruptAsync(FakeCodexCall call)
	{
		var threadId = call.ParamString("threadId") ?? string.Empty;
		var turnId   = call.ParamString("turnId") ?? string.Empty;
		await call.ReplyEmptyAsync().ConfigureAwait(false);

		if (!_playbacks.TryGetValue(turnId, out var playback))
			return;
		playback.Cancel.Cancel();
		await playback.Task.ConfigureAwait(false);
		await WriteLineAsync(new JsonObject
		{
			["method"] = "turn/completed",
			["params"] = new JsonObject { ["threadId"] = threadId, ["turn"] = Scripts.Turn(turnId, "interrupted") },
		}.ToJsonString()).ConfigureAwait(false);
	}

	private void Play(string turnId, List<FakeCodexStep> steps)
	{
		var cancel   = new CancellationTokenSource();
		var playback = new TurnPlayback(cancel) { Task = Task.Run(() => PlayStepsAsync(steps, cancel.Token)) };
		_playbacks[turnId] = playback;
	}

	private async Task PlayStepsAsync(List<FakeCodexStep> steps, CancellationToken cancel)
	{
		try
		{
			foreach (var step in steps)
			{
				cancel.ThrowIfCancellationRequested();
				switch (step.Kind)
				{
					case FakeCodexStepKind.Notify:
						await NotifyAsync(step.Method, step.Params!).ConfigureAwait(false);
						break;
					case FakeCodexStepKind.ServerRequest:
						await RequestAsync(step.Method, step.Params!).ConfigureAwait(false);
						break;
					case FakeCodexStepKind.Crash:
						Crash(step.ExitCode, step.Stderr ?? []);
						return;
					case FakeCodexStepKind.Hang:
						await Task.Delay(Timeout.Infinite, cancel).ConfigureAwait(false);
						return;
				}
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (IOException)
		{
		}
		catch (TimeoutException)
		{
		}
	}

	private sealed class TurnPlayback(CancellationTokenSource cancel)
	{
		public CancellationTokenSource Cancel { get; } = cancel;
		public Task                    Task   { get; set; } = Task.CompletedTask;
	}
}
