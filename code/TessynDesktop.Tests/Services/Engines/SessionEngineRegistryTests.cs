using System;
using Serilog.Events;
using TessynDesktop.Models;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Services.Engines;

/// <summary>FR.33.1: the router picks the engine by id (null and <c>claude</c> mean Claude) and asks every engine about a node's process.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionEngineRegistryTests
{
	private readonly FakeSessionEngine _claude = new(Constants.SessionEngines.Claude);
	private readonly FakeSessionEngine _other  = new("other");

	private SessionEngineRegistry Router() => new([_claude, _other]);

	private static SessionNodeModel Node(string? engineId) => new() { Name = "N", FileName = "n.txt", NodeId = "n1", EngineId = engineId };

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("claude")]
	[InlineData("CLAUDE")]
	public void Get_NullEmptyOrClaude_IsTheClaudeEngine(string? engineId) =>
		Assert.Same(_claude, Router().Get(engineId));

	[Fact]
	public void Get_AnotherRegisteredId_IsThatEngine() =>
		Assert.Same(_other, Router().Get("other"));

	[Fact]
	public void Get_AnUnknownId_Throws_NamingIt()
	{
		var ex = Assert.Throws<InvalidOperationException>(() => Router().Get("codex"));
		Assert.Contains("codex", ex.Message);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("claude")]
	public void For_ASessionWithoutAnEngineOrWithClaude_RunsOnClaude(string? engineId) =>
		Assert.Same(_claude, Router().For(Node(engineId)));

	[Fact]
	public void For_ASessionOnAnotherEngine_RunsOnIt() =>
		Assert.Same(_other, Router().For(Node("other")));

	[Fact]
	public void For_ASessionWithAnUnknownEngine_RunsOnClaude_AndWarnsOncePerNode()
	{
		var log    = new CapturingSink();
		var router = new SessionEngineRegistry([_claude, _other], log.CreateLogger());
		var first  = Node("codex");
		var second = new SessionNodeModel { Name = "M", FileName = "m.txt", NodeId = "n2", EngineId = "future" };

		Assert.Same(_claude, router.For(first));
		Assert.Same(_claude, router.For(first));
		Assert.Same(_claude, router.For(second));

		Assert.Equal(
		[
			"Unknown engine 'codex' on session n1; using Claude.",
			"Unknown engine 'future' on session n2; using Claude.",
		], log.Messages(LogEventLevel.Warning));
	}

	[Fact]
	public void Engines_AreListedInRegistrationOrder() =>
		Assert.Equal([_claude, _other], Router().Engines);

	[Fact]
	public void A_RouterWithoutClaude_IsRefused() =>
		Assert.Throws<ArgumentException>(() => new SessionEngineRegistry([_other]));

	[Fact]
	public void An_EngineRegisteredTwice_IsRefused() =>
		Assert.Throws<ArgumentException>(() => new SessionEngineRegistry([_claude, new FakeSessionEngine("Claude")]));

	[Fact]
	public void IsAlive_IsTrue_WhenAnyEngineHasTheNodesProcess()
	{
		var router = Router();
		Assert.False(router.IsAlive("n1"));

		_other.Alive.Add("n1");

		Assert.True(router.IsAlive("n1"));
		Assert.False(router.IsAlive("n2"));
	}

	[Fact]
	public void Stop_StopsTheNodeOnlyOnTheEngineThatHasItAlive()
	{
		_other.Alive.Add("n1");

		Router().Stop("n1");

		Assert.Equal(["n1"], _other.Stopped);
		Assert.Empty(_claude.Stopped);
		Assert.False(_other.IsAlive("n1"));
	}

	[Fact]
	public void TerminateAll_ReachesEveryEngine()
	{
		Router().TerminateAll();

		Assert.Equal(1, _claude.TerminateAllCount);
		Assert.Equal(1, _other.TerminateAllCount);
	}

	[Fact]
	public void The_ClaudeEngine_DelegatesLivenessAndStopToTheStreamingManager()
	{
		var streaming = new StubStreamingManager();
		streaming.Alive.Add("n1");
		var router = TestEngines.Claude(streaming);

		Assert.True(router.IsAlive("n1"));
		router.Stop("n1");

		Assert.Equal(["n1"], streaming.Stopped);
		Assert.False(router.IsAlive("n1"));
	}
}
