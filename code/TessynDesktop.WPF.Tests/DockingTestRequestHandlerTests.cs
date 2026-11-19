using System.Text.Json.Nodes;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;

namespace TessynDesktop.WPF.Tests;

/// <summary>FR.6.22, FR.29.26: the docking test hook's request parsing, dispatch and replies, over fake operations.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class DockingTestRequestHandlerTests
{
	private static JsonObject Reply(string json, FakeDockingTestOperations ops) => JsonNode.Parse(new DockingTestRequestHandler(ops).Handle(json))!.AsObject();

	[Theory]
	[InlineData("{\"op\":\"dock\",\"panel\":\"recent\",\"arg\":\"bottom\"}", "dock recent bottom")]
	[InlineData("{\"op\":\"tab\",\"panel\":\"tags\",\"arg\":\"session\"}", "tab tags session")]
	[InlineData("{\"op\":\"float\",\"panel\":\"focus\"}", "float focus")]
	[InlineData("{\"op\":\"float\",\"panel\":\"focus\",\"x\":10,\"y\":20,\"w\":300,\"h\":200}", "float focus 10 20 300 200")]
	[InlineData("{\"op\":\"float\",\"panel\":\"focus\",\"x\":10,\"y\":20}", "float focus")]
	[InlineData("{\"op\":\"autohide\",\"panel\":\"sessions\",\"arg\":\"on\"}", "autohide sessions on")]
	[InlineData("{\"op\":\"autohide\",\"panel\":\"sessions\",\"arg\":\"off\"}", "autohide sessions off")]
	[InlineData("{\"op\":\"slideout\",\"panel\":\"sessions\"}", "slideout sessions")]
	[InlineData("{\"op\":\"close\",\"panel\":\"tags\"}", "close tags")]
	[InlineData("{\"op\":\"show\",\"panel\":\"tags\"}", "show tags")]
	[InlineData("{\"op\":\"bringforward\",\"panel\":\"recent\"}", "bringforward recent")]
	[InlineData("{\"op\":\"reset\"}", "reset")]
	public void EveryOperation_IsDispatchedToTheMatchingCall(string request, string expected)
	{
		var ops = new FakeDockingTestOperations();

		var reply = Reply(request, ops);

		Assert.Equal(expected, Assert.Single(ops.Calls));
		Assert.True(reply["ok"]!.GetValue<bool>());
	}

	[Fact]
	public void State_CallsNothingAndReportsEveryPanel()
	{
		var ops = new FakeDockingTestOperations();
		ops.Panels.Add(new DockingPanelState { Id = "focus", Kind = "shell", Shown = true, Side = "floating", Floating = true, FloatRect = new DockingFloatRect(1, 2, 3, 4), Badge = "2" });

		var reply = Reply("{\"op\":\"state\"}", ops);

		Assert.Empty(ops.Calls);
		var panels = reply["panels"]!.AsArray();
		Assert.Equal(2, panels.Count);
		Assert.Equal("sessions", panels[0]!["id"]!.GetValue<string>());
		Assert.Equal(320, panels[0]!["width"]!.GetValue<double>());
		Assert.True(panels[1]!["floating"]!.GetValue<bool>());
		Assert.Equal(3, panels[1]!["floatRect"]!["width"]!.GetValue<double>());
		Assert.Equal("2", panels[1]!["badge"]!.GetValue<string>());
		Assert.Null(panels[0]!["badge"]);   // absent, not null
	}

	[Fact]
	public void ARefusedCall_IsAnErrorReply_WithTheReasonAndTheStateAfterIt()
	{
		var ops = new FakeDockingTestOperations { Result = new DockingTestResult(false, "no way") };

		var reply = Reply("{\"op\":\"close\",\"panel\":\"session\"}", ops);

		Assert.False(reply["ok"]!.GetValue<bool>());
		Assert.Equal("no way", reply["error"]!.GetValue<string>());
		Assert.Single(reply["panels"]!.AsArray());
	}

	[Theory]
	[InlineData("this is not json")]
	[InlineData("[1,2]")]
	[InlineData("{}")]
	[InlineData("{\"op\":\"\"}")]
	[InlineData("")]
	public void AnUnreadableRequest_IsAnErrorReply_AndNothingIsCalled(string request)
	{
		var ops = new FakeDockingTestOperations();

		var reply = Reply(request, ops);

		Assert.False(reply["ok"]!.GetValue<bool>());
		Assert.False(string.IsNullOrEmpty(reply["error"]!.GetValue<string>()));
		Assert.Empty(ops.Calls);
	}

	[Theory]
	[InlineData("{\"op\":\"explode\",\"panel\":\"x\"}", "explode")]
	[InlineData("{\"op\":\"dock\"}", "panel")]
	[InlineData("{\"op\":\"dock\",\"panel\":\"x\"}", "arg")]
	[InlineData("{\"op\":\"tab\",\"panel\":\"x\"}", "arg")]
	[InlineData("{\"op\":\"autohide\",\"panel\":\"x\"}", "arg")]
	[InlineData("{\"op\":\"autohide\",\"panel\":\"x\",\"arg\":\"maybe\"}", "maybe")]
	[InlineData("{\"op\":\"close\"}", "panel")]
	public void AnIncompleteOrUnknownRequest_IsRefusedWithAReasonThatNamesTheProblem(string request, string mentions)
	{
		var ops = new FakeDockingTestOperations();

		var reply = Reply(request, ops);

		Assert.False(reply["ok"]!.GetValue<bool>());
		Assert.Contains(mentions, reply["error"]!.GetValue<string>());
		Assert.Empty(ops.Calls);
	}

	[Fact]
	public void ACallThatThrows_BecomesAnErrorReply_AndTheHandlerKeepsWorking()
	{
		var ops = new FakeDockingTestOperations { Throw = new InvalidOperationException("boom") };
		var handler = new DockingTestRequestHandler(ops);

		var first = JsonNode.Parse(handler.Handle("{\"op\":\"reset\"}"))!.AsObject();
		ops.Throw = null;
		var second = JsonNode.Parse(handler.Handle("{\"op\":\"reset\"}"))!.AsObject();

		Assert.False(first["ok"]!.GetValue<bool>());
		Assert.Contains("boom", first["error"]!.GetValue<string>());
		Assert.True(second["ok"]!.GetValue<bool>());
	}

	[Fact]
	public void TheReply_IsOneLine()
	{
		var ops = new FakeDockingTestOperations();

		Assert.DoesNotContain('\n', new DockingTestRequestHandler(ops).Handle("{\"op\":\"state\"}"));
	}
}
