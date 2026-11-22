using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TessynDesktop.Codex;
using TessynDesktop.Models;
using TessynDesktop.Models.Turns;
using TessynDesktop.Services;
using TessynDesktop.Tests.Codex.Fakes;
using TessynDesktop.Tests.Services;
using TessynDesktop.Tests.Services.Turns;

namespace TessynDesktop.Tests.Codex.Engine;

/// <summary>
/// Both pipelines over a router with the Claude engine (the recording Claude fakes of <see cref="TurnEngineHarness"/>) and the Codex engine (the
/// in-process fake app-server): <see cref="Interactive"/> is a <see cref="TurnEngine"/> with a real <see cref="SessionCompactionService"/>,
/// <see cref="Headless"/> is a real <see cref="SessionTurnService"/>. They share the transcript, the settings and the turn locks of the harness.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class CodexPipelineHarness : IAsyncDisposable
{
	public CodexPipelineHarness()
	{
		H = new TurnEngineHarness(agentTools: true);
		Pool   = new CodexHostPool(Launcher, time: Clock, executablePath: CodexEngineFixture.Executable);
		var config = new CodexThreadConfigBuilder(H.Settings, new Lazy<IAgentMcpServer>(() => Mcp), new McpConfigMerger(new FakeMcpOAuthService()));
		Codex  = new CodexSessionEngine(Pool, config, H.Files, H.Settings);
		Claude = new ClaudeSessionEngine(H.Process, H.Streaming, H.Files, H.Settings, H.Status, new Lazy<IAgentMcpServer>(() => H.Mcp));
		Router = new SessionEngineRegistry([Claude, Codex]);

		Compaction  = new SessionCompactionService(H.Process, H.Files, H.Settings, H.Registry, H.Turns, Router);
		Interactive = new TurnEngine(H.Files, H.Registry, H.Turns, Router, H.Settings, new Lazy<ITelegramBridgeService>(() => H.Telegram),
			H.Attention.Service, H.Tags.Service, [Progress], compaction: Compaction, authErrors: H.AuthErrors);

		var profiles = new FakeProfileServiceForTurnService();
		Headless = new SessionTurnService(Router, H.Files, H.Settings, profiles, new FakeRegistryForTurnService(), new FakeModelServiceForTurnService(),
			new Lazy<ITelegramBridgeService>(() => H.Telegram), H.Attention.Service, H.Tags.Service, new ProfileLabelService(profiles, H.Settings), [Progress]);
	}

	public TurnEngineHarness H { get; }
	public FakeCodexProcessLauncher Launcher { get; } = new();
	public ManualTimerTimeProvider Clock { get; } = new();
	public FakeCmxMcpServer Mcp { get; } = new();
	public RecordingProgressSink Progress { get; } = new();
	public CodexHostPool Pool { get; }
	public CodexSessionEngine Codex { get; }
	public ClaudeSessionEngine Claude { get; }
	public SessionEngineRegistry Router { get; }
	public SessionCompactionService Compaction { get; }
	public TurnEngine Interactive { get; }
	public SessionTurnService Headless { get; }

	public FakeCodexAppServer Server => Launcher.Latest;

	/// <summary>A Codex session in the harness's settings tree (so saving it works), with the harness's node id.</summary>
	public SessionNodeModel CodexNode(string? threadId = null, string? claudeSessionId = null)
	{
		var node = TurnEngineHarness.Node(claudeSessionId);
		node.EngineId      = Constants.SessionEngines.Codex;
		node.CodexThreadId = threadId;
		H.Settings.Settings.Tree.Add(new DirectoryNodeModel { Path = node.WorkingDirectory, Sessions = [node] });
		return node;
	}

	public static TurnSettings Gpt(string model = "gpt-6.2-sol", string? effort = "low") => new(model, effort, null, "ChatGPT", null, false, false, null, null);

	public Task<TurnOutcome> RunInteractiveAsync(SessionNodeModel node, string message, TurnSettings settings, ITurnObserver? observer = null, bool autoCompact = false) =>
		Interactive.RunAsync(new TurnRequest(node, message, settings, false, AutoCompact: autoCompact), observer).WaitAsync(TimeSpan.FromSeconds(20));

	public JsonElement[] Requests(string method) => [.. Launcher.Servers.SelectMany(s => s.RequestsOf(method)).Select(m => m.Params)];

	public async ValueTask DisposeAsync()
	{
		await Pool.DisposeAsync();
		H.Dispose();
	}
}
