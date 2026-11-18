using System;
using System.IO;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using TessynDesktop.Tests.Services;
using TessynDesktop.Tests.Services.Turns;

namespace TessynDesktop.Tests.Contract;

/// <summary>
/// The whole turn stream over a real loopback <see cref="TdcpServer"/> (P1.I stage 3): a real registry and event bus, the real engine, send
/// service, settings resolver, publisher and RPC module, with fakes only for the claude processes and the files. The file service's
/// <c>TurnCompleted</c> is turned into <c>turn.completed</c> as <c>RemoteNotificationPublisher</c> does. Disposing deletes the temp dir.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class TurnWireEnv : IDisposable
{
	private readonly string _dir;

	public AppSettingsService Settings { get; }
	public InMemorySessionFileService Files { get; } = new();
	public SessionRegistry Registry { get; }
	public RemoteEventBus Bus { get; } = new();
	public TurnEventPublisher Publisher { get; }
	public ScriptedProcessManager Process { get; } = new();
	public ScriptedStreamingManager Streaming { get; } = new();
	public SharedLockTurnService Turns { get; } = new();
	public FakeModelServiceForTurnService Models { get; } = new();
	public FakeProfileServiceForTurnService ProfileService { get; } = new();
	public AttentionTestEnv Attention { get; } = new();
	public TagTestEnv Tags { get; } = new();
	public TurnEngine Engine { get; }
	public TdcpDispatcher Dispatcher { get; }
	public TdcpTestHost Host { get; }
	public SessionNodeModel Session { get; }

	public TurnWireEnv(bool withEngineCapability = true)
	{
		_dir = Path.Combine(Path.GetTempPath(), "turn-wire-tests-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_dir);

		Settings = new AppSettingsService(_dir);
		Settings.Settings.SessionFilesRoot = _dir;
		Settings.Settings.ClaudePath       = "claude-test";

		Registry = new SessionRegistry(Settings, Files, new FakeDirectoryLabelService(), new FakeClaudeSessionStatusService());
		var directory = Registry.AddDirectory(Path.Combine(_dir, "proj"));
		Session = Registry.AddSession(directory, "Wire session");

		Publisher = new TurnEventPublisher(Bus, TimeSpan.FromMilliseconds(20));
		Engine = new TurnEngine(
			Files, Registry, Turns, Process, Streaming, Settings,
			new Lazy<IAgentMcpServer>(() => new RecordingMcpServer()), new Lazy<ITelegramBridgeService>(() => new RecordingTelegramBridge()),
			new RecordingSessionStatusService(), Attention.Service, Tags.Service, [Publisher]);

		var labels   = new ProfileLabelService(ProfileService, Settings);
		var options  = new TurnOptionsResolver(Models, Settings, labels);
		var resolver = new TurnSettingsResolver(Settings, Turns, ProfileService, Models, labels);
		var module   = new TurnRpcModule(Registry, options, resolver, new TurnSendService(Engine, Registry), Engine, Turns, Streaming, new SessionModelSettings(Settings));

		// The event log of hello's capabilities (the host keeps its own for the stream).
		// The run id of hello must be the one of the stream the client then resumes on.
		var runInfo  = new DaemonRunInfo();
		var helloLog = new EventLog(Bus);
		Dispatcher = new TdcpDispatcher([new ClientRpcModule(runInfo: runInfo, eventLog: helloLog, turnEngine: withEngineCapability ? Engine : null), module]);
		Host = new TdcpTestHost(Dispatcher, bus: Bus, runInfo: runInfo);

		Files.TurnCompleted += (_, e) =>
		{
			var node = Registry.FindModelByFileName(e.FileName);
			if (node != null)
				Bus.Publish(RemoteProtocol.Events.TurnCompleted, new TurnCompletedPayload(node.NodeId, e.Cancelled, e.ErrorText, e.TurnId));
		};
	}

	public string NodeId => Session.NodeId;

	/// <summary>Reads SSE frames until a <c>turn.completed</c> arrives, returning every frame up to and including it.</summary>
	public static async Task<System.Collections.Generic.List<SseTestFrame>> ReadUntilCompletedAsync(SseTestClient sse, string? turnId = null)
	{
		var frames = new System.Collections.Generic.List<SseTestFrame>();
		while (true)
		{
			var frame = await sse.NextAsync();
			frames.Add(frame);
			if (frame.Method == RemoteProtocol.Events.TurnCompleted
			    && (turnId == null || frame.Params!["turnId"]?.GetValue<string>() == turnId))
				return frames;
		}
	}

	public void Dispose()
	{
		Host.Dispose();
		Publisher.Dispose();
		Attention.Dispose();
		Tags.Dispose();
		if (Directory.Exists(_dir))
			Directory.Delete(_dir, recursive: true);
	}
}
