using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Turns;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Services.Turns;

/// <summary>
/// A <see cref="TurnEngine"/> over real settings, real attention and tag services (temp dirs) and the recording fakes of
/// <c>TurnEngineFakes</c>. Disposing deletes the temp dirs.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class TurnEngineHarness : IDisposable
{
	public const string NodeId = "node-1";

	private readonly string _dir;

	public AppSettingsService Settings { get; }
	public ScriptedProcessManager Process { get; } = new();
	public ScriptedStreamingManager Streaming { get; } = new();
	public InMemorySessionFileService Files { get; } = new();
	public FakeRegistryForTurnService Registry { get; } = new();
	public SharedLockTurnService Turns { get; } = new();
	public RecordingSessionStatusService Status { get; } = new();
	public RecordingMcpServer Mcp { get; } = new();
	public RecordingTelegramBridge Telegram { get; } = new();
	public AttentionTestEnv Attention { get; } = new();
	public TagTestEnv Tags { get; } = new();
	public TurnEngine Engine { get; }

	public TurnEngineHarness(bool agentTools = false, System.Collections.Generic.IEnumerable<ITurnProgressSink>? sinks = null)
	{
		_dir = Path.Combine(Path.GetTempPath(), $"turn-engine-tests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_dir);
		Settings = new AppSettingsService(_dir);
		Settings.Settings.SessionFilesRoot  = _dir;
		Settings.Settings.AgentToolsEnabled = agentTools;
		Settings.Settings.ClaudePath        = "claude-test";

		Files.LockCount = () => Turns.GetTurnLock(NodeId).CurrentCount;

		Engine = new TurnEngine(
			Files, Registry, Turns, Process, Streaming, Settings,
			new Lazy<IAgentMcpServer>(() => Mcp), new Lazy<ITelegramBridgeService>(() => Telegram),
			Status, Attention.Service, Tags.Service, sinks);
	}

	public static SessionNodeModel Node(string? sessionId = null, bool streaming = false, int depth = 0) => new()
	{
		NodeId             = NodeId,
		Name               = "Test",
		FileName           = $"{NodeId}.txt",
		WorkingDirectory   = @"C:\proj",
		AgentToken         = "tok",
		ClaudeSessionId    = sessionId,
		IsStreamingMode    = streaming,
		OrchestrationDepth = depth,
	};

	public static TurnSettings Chosen(string? model = "opus", string? effort = "high", string? profileDir = @"C:\profiles\p1",
		string? profileName = "Work", bool disableTools = false, bool chrome = false, ClaudeModelInfo? modelInfo = null,
		string? ollamaUrl = null) =>
		new(model, effort, profileDir, profileName, ollamaUrl, disableTools, chrome, modelInfo, null);

	public Task<TurnOutcome> Run(SessionNodeModel node, string message = "Hi", ITurnObserver? observer = null, TurnSettings? settings = null,
		bool isNewBranch = false, CancellationToken ct = default) =>
		Engine.RunAsync(new TurnRequest(node, message, settings ?? Chosen(), isNewBranch), observer, ct);

	/// <summary>What the former composer would have built for the same inputs (the oracle the engine's block is compared with).</summary>
	public string ExpectedBlock(SessionNodeModel node, bool isNewBranch, bool isNewSession, bool? streamingOverride = null)
	{
		var options = new InstructionOptionsModel(
			node.IsAutoCommit, isNewBranch, node.IsAutoDocument,
			Settings.Settings.AgentToolsEnabled, isNewSession, streamingOverride ?? node.IsStreamingMode,
			IsTelegramEnabled: Settings.Settings.TelegramEnabled,
			IsWorkerSession: node.OrchestrationDepth > 0);
		return InstructionBlockBuilder.Build(options);
	}

	public void Dispose()
	{
		Attention.Dispose();
		Tags.Dispose();
		if (Directory.Exists(_dir))
			Directory.Delete(_dir, recursive: true);
	}
}
