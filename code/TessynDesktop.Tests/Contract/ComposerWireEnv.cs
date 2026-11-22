using System;
using System.IO;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using TessynDesktop.Tests.Services;
using TessynDesktop.Tests.Services.Turns;

namespace TessynDesktop.Tests.Contract;

/// <summary>
/// Package J2 over a real loopback <see cref="TdcpServer"/>: the real engine and send service (with a recording compaction service and the real auth
/// error tracker), the draft module over a real <see cref="DraftService"/> in a temp dir, and the autocomplete module over a <see cref="FakeCodeIndexService"/>.
/// Disposing deletes the temp dir.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class ComposerWireEnv : IDisposable
{
	private readonly string _dir;

	public AppSettingsService Settings { get; }
	public InMemorySessionFileService Files { get; } = new();
	public SessionRegistry Registry { get; }
	public ScriptedProcessManager Process { get; } = new();
	public ScriptedStreamingManager Streaming { get; } = new();
	public SharedLockTurnService Turns { get; } = new();
	public AttentionTestEnv Attention { get; } = new();
	public TagTestEnv Tags { get; } = new();
	public RecordingCompactionService Compaction { get; }
	public SessionAuthErrorTracker AuthErrors { get; } = new();
	public DraftService Drafts { get; }
	public FakeCodeIndexService CodeIndex { get; } = new();
	public RemoteEventBus Bus { get; } = new();
	public TurnEventPublisher Publisher { get; }
	public TurnEngine Engine { get; }
	public AutocompleteRpcModule Autocomplete { get; }
	public TdcpDispatcher Dispatcher { get; }
	public TdcpTestHost Host { get; }
	public SessionNodeModel Session { get; }
	public string ProjectDirectory { get; }

	public ComposerWireEnv(bool withDrafts = true, bool withCodeIndex = true)
	{
		_dir = Path.Combine(Path.GetTempPath(), "composer-wire-tests-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_dir);

		Settings = new AppSettingsService(_dir);
		Settings.Settings.SessionFilesRoot = _dir;
		Settings.Settings.ClaudePath       = "claude-test";

		Registry = new SessionRegistry(Settings, Files, new FakeDirectoryLabelService(), new FakeClaudeSessionStatusService());
		ProjectDirectory = Path.Combine(_dir, "proj");
		var directory = Registry.AddDirectory(ProjectDirectory);
		Session = Registry.AddSession(directory, "Composer session");

		Compaction = new RecordingCompactionService(() => Turns.GetTurnLock(Session.NodeId).CurrentCount, Files.Log);
		Drafts     = new DraftService(Path.Combine(_dir, "drafts"));

		Publisher = new TurnEventPublisher(Bus, TimeSpan.FromMilliseconds(20));
		Engine = new TurnEngine(
			Files, Registry, Turns, TestEngines.Claude(Streaming, Process, Files, Settings, new RecordingSessionStatusService(), new Lazy<IAgentMcpServer>(() => new RecordingMcpServer())), Settings, new Lazy<ITelegramBridgeService>(() => new RecordingTelegramBridge()), Attention.Service, Tags.Service, [Publisher], compaction: Compaction, authErrors: AuthErrors);

		var models   = new FakeModelServiceForTurnService();
		var profiles = new FakeProfileServiceForTurnService();
		var labels   = new ProfileLabelService(profiles, Settings);
		var options  = new TurnOptionsResolver(models, Settings, labels);
		var resolver = new TurnSettingsResolver(Settings, Turns, profiles, models, labels);
		var turnModule = new TurnRpcModule(Registry, options, resolver, new TurnSendService(Engine, Registry), Engine, Turns, TestEngines.Claude(Streaming), new SessionModelSettings(Settings));

		Autocomplete = new AutocompleteRpcModule(Registry, CodeIndex);

		var runInfo  = new DaemonRunInfo();
		var bus      = Bus;
		var helloLog = new EventLog(bus);
		var hello    = new ClientRpcModule(
			runInfo: runInfo, eventLog: helloLog, turnEngine: Engine,
			drafts: withDrafts ? Drafts : null, codeIndex: withCodeIndex ? CodeIndex : null, authErrors: AuthErrors);

		Dispatcher = new TdcpDispatcher([hello, turnModule, new DraftRpcModule(Registry, Drafts, Attention.Service), Autocomplete]);
		Host = new TdcpTestHost(Dispatcher, bus: bus, runInfo: runInfo);

		// The file service's TurnCompleted becomes turn.completed, as RemoteNotificationPublisher does.
		Files.TurnCompleted += (_, e) =>
		{
			var node = Registry.FindModelByFileName(e.FileName);
			if (node != null)
				Bus.Publish(RemoteProtocol.Events.TurnCompleted, new TurnCompletedPayload(node.NodeId, e.Cancelled, e.ErrorText, e.TurnId));
		};
	}

	public string NodeId => Session.NodeId;

	public void Dispose()
	{
		Host.Dispose();
		Publisher.Dispose();
		Autocomplete.Dispose();
		Attention.Dispose();
		Tags.Dispose();
		if (Directory.Exists(_dir))
			Directory.Delete(_dir, recursive: true);
	}
}
