using System;
using System.Threading.Tasks;
using TessynDesktop.Models;
using Xunit;

namespace TessynDesktop.Tests.Services.Turns;

/// <summary>
/// Safety net for the Focus / tag side effects of an interactive turn (P1.I stage 1, FR.23.11, FR.24.6): which attention and tag lines the
/// instruction block carries, and that taking them consumes the once-only state exactly as the former composer did.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TurnEngineFocusTests : IDisposable
{
	private readonly TurnEngineHarness _h = new(agentTools: true);

	public void Dispose() => _h.Dispose();

	private async Task<string> SentMessageAsync(SessionNodeModel node)
	{
		_h.Process.ScriptEvents(Evt.Result());
		await _h.Run(node).Within();
		return _h.Process.Calls[^1].Message;
	}

	[Fact]
	public async Task TopLevelSession_ListsItsOpenAttentionItemsInThePinnedList()
	{
		var node = TurnEngineHarness.Node();
		_h.Attention.Service.Post(node, [AttentionTestEnv.Req("Decide the schema")]);

		var sent = await SentMessageAsync(node);

		Assert.Contains(Constants.Attention.PinnedHeaderLine, sent);
		Assert.Contains("Decide the schema", sent);
	}

	[Fact]
	public async Task WorkerSession_GetsNeitherAttentionNorTagLines_AndTheirOnceOnlyStateIsNotConsumed()
	{
		var node = TurnEngineHarness.Node(depth: 1);
		_h.Attention.Service.Post(node, [AttentionTestEnv.Req("Worker item")]);

		var sent = await SentMessageAsync(node);

		Assert.DoesNotContain(Constants.Attention.PinnedHeaderLine, sent);
		Assert.DoesNotContain(Constants.Instructions.SessionTagGuidance, sent);
		Assert.True(_h.Tags.Service.TakeTagGuidanceDue(TurnEngineHarness.NodeId));
	}

	[Fact]
	public async Task AgentToolsOff_GetsNoAttentionLines_AndNothingIsConsumed()
	{
		using var off = new TurnEngineHarness(agentTools: false);
		var node = TurnEngineHarness.Node();
		off.Attention.Service.Post(node, [AttentionTestEnv.Req("Hidden")]);
		off.Process.ScriptEvents(Evt.Result());

		await off.Run(node).Within();

		Assert.DoesNotContain("Hidden", off.Process.Calls[0].Message);
		Assert.True(off.Tags.Service.TakeTagGuidanceDue(TurnEngineHarness.NodeId));
	}

	[Fact]
	public async Task TagGuidance_IsSentOnTheFirstTurnOfTheRunOnly()
	{
		var node = TurnEngineHarness.Node(sessionId: "s1");

		var first  = await SentMessageAsync(node);
		var second = await SentMessageAsync(node);

		Assert.Contains(Constants.Instructions.SessionTagGuidance, first);
		Assert.DoesNotContain(Constants.Instructions.SessionTagGuidance, second);
	}

	[Fact]
	public async Task ClosureByTheUser_IsReportedOnTheNextTurnOnly()
	{
		var node = TurnEngineHarness.Node(sessionId: "s1");
		var outcome = _h.Attention.Service.Post(node, [AttentionTestEnv.Req("Question one")]);
		_h.Attention.Service.CloseByUser(node, outcome[0].Item.Id);

		var first  = await SentMessageAsync(node);
		var second = await SentMessageAsync(node);

		Assert.Contains(Constants.Attention.ClosuresLinePrefix, first);
		Assert.DoesNotContain(Constants.Attention.ClosuresLinePrefix, second);
	}

	[Fact]
	public async Task NewSession_AddsTheOnceOnlyBans_ExistingSessionDoesNot()
	{
		var fresh    = await SentMessageAsync(TurnEngineHarness.Node());
		var existing = await SentMessageAsync(TurnEngineHarness.Node(sessionId: "s1"));

		Assert.Contains(Constants.Instructions.NoWorkflowTool, fresh);
		Assert.DoesNotContain(Constants.Instructions.NoWorkflowTool, existing);
	}

	[Fact]
	public async Task StreamingTurn_OmitsTheNativeSchedulingRedirect()
	{
		var node = TurnEngineHarness.Node(streaming: true);
		_h.Streaming.ScriptEvents(Evt.Result());

		await _h.Run(node).Within();

		var sent = _h.Streaming.Sends[0].Message;
		Assert.DoesNotContain(Constants.Instructions.NativeSchedulingRedirect, sent);
		Assert.Contains(Constants.Instructions.NoWorkflowTool, sent);
	}

	[Fact]
	public async Task OneShotTurn_CarriesTheNativeSchedulingRedirect()
	{
		var sent = await SentMessageAsync(TurnEngineHarness.Node());

		Assert.Contains(Constants.Instructions.NativeSchedulingRedirect, sent);
	}
}
