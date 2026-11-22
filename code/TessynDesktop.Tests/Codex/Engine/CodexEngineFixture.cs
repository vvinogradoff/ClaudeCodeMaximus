using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Codex;
using TessynDesktop.Models;
using TessynDesktop.Models.Engines;
using TessynDesktop.Models.Turns;
using TessynDesktop.Services;
using TessynDesktop.Tests.Codex.Fakes;
using TessynDesktop.Tests.Services.Engines;
using TessynDesktop.Tests.Services.Turns;

namespace TessynDesktop.Tests.Codex.Engine;

/// <summary>
/// A <see cref="CodexSessionEngine"/> over the in-process fake app-server (FR.33.19): real settings on a temp data root, the in-memory transcript,
/// a <see cref="CodexHostPool"/> on the fake launcher and its own clock, and a separate engine clock for the 15 s Stop timeout. Disposing stops
/// the hosts and deletes the temp folder.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class CodexEngineFixture : IAsyncDisposable
{
	public const string Executable = @"C:\Codex\codex-x86_64-pc-windows-msvc.exe";
	public const string WorkingDirectory = @"C:\Proj\Alpha";

	private readonly FixedProfileSelector _profiles;
	private int _nodeCounter;

	public CodexEngineFixture(bool agentTools = true, string profileId = Constants.Codex.Profiles.DefaultProfileId, string? codexHome = null,
		ISessionRegistry? registry = null)
	{
		Dir = Path.Combine(Path.GetTempPath(), $"codex-engine-tests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(Dir);
		Settings = new AppSettingsService(Dir);
		Settings.Settings.SessionFilesRoot  = Dir;
		Settings.Settings.AgentToolsEnabled = agentTools;

		Pool     = new CodexHostPool(Launcher, time: HostClock, executablePath: Executable);
		Config   = new CodexThreadConfigBuilder(Settings, new Lazy<IAgentMcpServer>(() => Mcp), new McpConfigMerger(OAuth));
		_profiles = new FixedProfileSelector(new CodexProfileRef(profileId, codexHome));
		Engine   = new CodexSessionEngine(Pool, Config, Files, Settings, registry, _profiles, EngineClock);
	}

	public string Dir { get; }
	public AppSettingsService Settings { get; }
	public InMemorySessionFileService Files { get; } = new();
	public FakeCodexProcessLauncher Launcher { get; } = new();
	public ManualTimerTimeProvider HostClock { get; } = new();
	public ManualTimerTimeProvider EngineClock { get; } = new();
	public FakeCmxMcpServer Mcp { get; } = new();
	public FakeMcpOAuthService OAuth { get; } = new();
	public CodexHostPool Pool { get; }
	public CodexThreadConfigBuilder Config { get; }
	public CodexSessionEngine Engine { get; }
	public Eventually Eventually { get; } = new();

	/// <summary>The profile every turn runs on (tests switch it to check the one-host-per-profile rule).</summary>
	public CodexProfileRef Profile
	{
		get => _profiles.Profile;
		set => _profiles.Profile = value;
	}

	public FakeCodexAppServer Server => Launcher.Latest;

	public FakeCodexScripts Scripts => Launcher.Scripts;

	/// <summary>A Codex session (EngineId <c>codex</c>) with its own node id, agent token and transcript file.</summary>
	public SessionNodeModel Node(string? threadId = null, string? name = null)
	{
		var n = Interlocked.Increment(ref _nodeCounter);
		var node = new SessionNodeModel
		{
			NodeId           = $"codex-node-{n}",
			Name             = name ?? $"GPT session {n}",
			FileName         = $"codex-node-{n}.txt",
			WorkingDirectory = WorkingDirectory,
			AgentToken       = $"TOKEN-{n}-{Guid.NewGuid():N}",
			EngineId         = Constants.SessionEngines.Codex,
			CodexThreadId    = threadId,
		};
		Settings.Settings.Tree.Add(new DirectoryNodeModel { Path = WorkingDirectory, Sessions = [node] });
		return node;
	}

	public static TurnSettings Chosen(string? model = "gpt-6.2-sol", string? effort = "low") =>
		new(model, effort, null, null, null, false, false, null, null);

	/// <summary>Runs one turn the way a pipeline does: the preparation first, then the turn.</summary>
	public async Task<RecordingEngineSink> RunAsync(SessionNodeModel node, string prompt = "Hi", TurnSettings? settings = null, string? augmented = null,
		CancellationToken ct = default, RecordingEngineSink? sink = null)
	{
		sink ??= new RecordingEngineSink();
		var chosen = settings ?? Chosen();
		var preparation = await Engine.PrepareTurnAsync(node, chosen);
		await Engine.RunTurnAsync(new EngineTurnRequest(node, prompt, augmented ?? prompt, node.CodexThreadId, chosen, preparation, EngineTurnMode.Interactive), sink, ct)
			.WaitAsync(TimeSpan.FromSeconds(20));
		return sink;
	}

	/// <summary>The params of every request of <paramref name="method"/> the given (default: latest) fake server got, in order.</summary>
	public JsonElement[] Requests(string method, FakeCodexAppServer? server = null) =>
		[.. (server ?? Server).RequestsOf(method).Select(m => m.Params)];

	/// <summary>The text of the first input item of a <c>turn/start</c>.</summary>
	public static string InputText(JsonElement turnStart) => turnStart.GetProperty("input")[0].GetProperty("text").GetString()!;

	public async ValueTask DisposeAsync()
	{
		await Pool.DisposeAsync();
		try
		{
			if (Directory.Exists(Dir))
				Directory.Delete(Dir, recursive: true);
		}
		catch (IOException)
		{
		}
	}
}
