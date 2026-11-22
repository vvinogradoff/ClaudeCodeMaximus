using System;
using System.Linq;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Turns;
using TessynDesktop.Services;
using TessynDesktop.Tests.Services.Turns;

namespace TessynDesktop.Tests.Services.Engines;

/// <summary>
/// FR.33.1, FR.33.4: <see cref="SessionNodeModel.EngineId"/> is only a stored cache of the engine of the resolved model. An id this build does
/// not know never fails a turn, and both pipelines resolve the engine before they write anything to the transcript.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class EngineResolutionTests : IDisposable
{
	private static readonly string _userHi = $"append:{Constants.SessionFile.RoleUser}:Hi";

	private readonly TurnEngineHarness _h = new();

	public void Dispose() => _h.Dispose();

	private SessionEngineRegistry RealRouter() =>
		TestEngines.Claude(_h.Streaming, _h.Process, _h.Files, _h.Settings, _h.Status, new Lazy<IAgentMcpServer>(() => _h.Mcp));

	private SessionTurnService HeadlessService(ISessionEngineRouter router)
	{
		var profiles = new FakeProfileServiceForTurnService();
		return new SessionTurnService(router, _h.Files, _h.Settings, profiles, new FakeRegistryForTurnService(), new FakeModelServiceForTurnService(),
			new Lazy<ITelegramBridgeService>(() => _h.Telegram), _h.Attention.Service, _h.Tags.Service, new ProfileLabelService(profiles, _h.Settings));
	}

	[Fact]
	public async Task An_interactive_turn_on_a_session_with_an_unknown_engine_runs_on_claude()
	{
		var node = TurnEngineHarness.Node();
		node.EngineId = "codex";
		_h.Process.ScriptEvents(Evt.Assistant("answer"), Evt.Result("s1"));

		var outcome = await _h.Run(node, "Hi").Within();

		Assert.Null(outcome.ErrorText);
		Assert.False(outcome.Cancelled);
		Assert.Single(_h.Process.Calls);
		Assert.Contains(_h.Files.Entries, e => e.Role == Constants.SessionFile.RoleAssistant && e.Content == "answer");
		Assert.Equal("codex", node.EngineId); // the stored id is left as it is
	}

	[Fact]
	public async Task A_headless_turn_on_a_session_with_an_unknown_engine_runs_on_claude()
	{
		var node = TurnEngineHarness.Node();
		node.EngineId = "codex";
		_h.Process.ScriptEvents(Evt.Assistant("answer"), Evt.Result("s1"));

		var result = await HeadlessService(RealRouter()).RunTurnAsync(node, "Hi", TurnSource.Scheduled).Within();

		Assert.False(result.IsError);
		Assert.Equal("answer", result.AssistantText);
		Assert.Single(_h.Process.Calls);
	}

	[Fact]
	public async Task The_interactive_turn_resolves_the_engine_before_it_writes_the_user_entry()
	{
		var router = new OrderRecordingRouter(RealRouter(), _h.Files.Log);
		var engine = new TurnEngine(_h.Files, _h.Registry, _h.Turns, router, _h.Settings, new Lazy<ITelegramBridgeService>(() => _h.Telegram),
			_h.Attention.Service, _h.Tags.Service);
		_h.Process.ScriptEvents(Evt.Result("s1"));

		await engine.RunAsync(new TurnRequest(TurnEngineHarness.Node(), "Hi", TurnEngineHarness.Chosen())).Within();

		AssertResolvedBeforeTheUserEntry();
	}

	[Fact]
	public async Task The_headless_turn_resolves_the_engine_before_it_writes_the_user_entry()
	{
		var router = new OrderRecordingRouter(RealRouter(), _h.Files.Log);
		_h.Process.ScriptEvents(Evt.Result("s1"));

		await HeadlessService(router).RunTurnAsync(TurnEngineHarness.Node(), "Hi", TurnSource.Scheduled).Within();

		AssertResolvedBeforeTheUserEntry();
	}

	private void AssertResolvedBeforeTheUserEntry()
	{
		var log      = _h.Files.Log.ToList();
		var resolved = log.IndexOf(OrderRecordingRouter.ResolvedMarker);
		var user     = log.IndexOf(_userHi);

		Assert.True(resolved >= 0, "the engine was never resolved");
		Assert.True(user >= 0, "the user entry was never written");
		Assert.True(resolved < user, $"the engine was resolved after the user entry: {string.Join(" | ", log)}");
	}
}
