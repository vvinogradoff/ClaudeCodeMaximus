using System.Text.Json.Nodes;
using Tessyn.Web.E2E.Tests.Harness;
using TessynDesktop.Models;

namespace Tessyn.Web.E2E.Tests;

/// <summary>FR.23.33-FR.23.36: a Focus questionnaire as the Web client sees and answers it, through the real stack.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class StackQuestionnaireFlowTests
{
	private const string Title = "Approve the migration plan?";

	private static JsonNode P(string json) => JsonNode.Parse(json)!;

	private static AttentionQuestionModel[] Questions() =>
	[
		new() { Number = 1, Type = AttentionQuestionType.YesNo, Text = "Run it during business hours?", Recommended = "no" },
		new() { Number = 2, Type = AttentionQuestionType.Single, Text = "Which order?", Choices = ["Staging first", "Production first"], Recommended = "Staging first" },
		new() { Number = 3, Type = AttentionQuestionType.Multi, Text = "Which tables?", Choices = ["Products", "Orders", "Customers"], RecommendedChoices = ["Products", "Orders"] },
		new() { Number = 4, Type = AttentionQuestionType.Text, Text = "Rollback notes?", Recommended = "Keep the snapshot." },
	];

	[Fact]
	public async Task F12_Questionnaire_Wire_Shape_And_Answers()
	{
		await using var stack = await WebStack.StartAsync();
		var browser = await stack.LoginAsync();
		var seed = stack.Seed;
		await using var events = await SseClient.OpenAsync(browser, "/api/events");
		await events.WaitForMethodAsync("link.status");

		var node = stack.Daemon.Registry.FindModelByNodeId(seed.SupervisorId)!;
		var outcome = Assert.Single(stack.Daemon.Attention.Post(node, [new AttentionPostRequestModel { Kind = AttentionKind.Ask, Title = Title, Body = "Plan body.", Questions = Questions() }]));
		var itemId = outcome.Item.Id;

		// attention.list carries the questionnaire in the contract shape the SPA codes against (camelCase, string enums).
		var list = await stack.RpcAsync(browser, "attention.list", P($$"""{"nodeId":"{{seed.SupervisorId}}"}"""));
		var item = list["items"]!.AsArray().Single(i => i!["id"]!.GetValue<string>() == itemId)!;
		var questions = item["questions"]!.AsArray();
		Assert.Equal(["yesNo", "single", "multi", "text"], questions.Select(q => q!["type"]!.GetValue<string>()).ToArray());
		Assert.Equal([1, 2, 3, 4], questions.Select(q => q!["number"]!.GetValue<int>()).ToArray());
		Assert.Equal("no", questions[0]!["recommended"]!.GetValue<string>());
		Assert.Equal(["Staging first", "Production first"], questions[1]!["choices"]!.AsArray().Select(c => c!.GetValue<string>()).ToArray());
		Assert.Equal(["Products", "Orders"], questions[2]!["recommendedChoices"]!.AsArray().Select(c => c!.GetValue<string>()).ToArray());
		Assert.Equal("Keep the snapshot.", questions[3]!["recommended"]!.GetValue<string>());

		// attention.reply with a changed Single, the recommended Multi, and a text answer: the item is answered with a note-less entry.
		var reply = await stack.RpcAsync(browser, "attention.reply", P($$"""
			{"nodeId":"{{seed.SupervisorId}}","answers":[{"itemId":"{{itemId}}","text":"","questions":[
				{"number":1,"value":"no"},{"number":2,"value":"Production first"},{"number":3,"choices":["Products","Orders"]},{"number":4,"value":"Changed notes."}]}]}
			"""));
		Assert.Equal([itemId], reply["answeredIds"]!.AsArray().Select(i => i!.GetValue<string>()).ToArray());
		await events.WaitForMethodAsync("turn.completed");

		var prompt = Assert.Single(stack.Daemon.Process.Snapshot()).Prompt;
		Assert.Contains("Q1 Run it during business hours?: No (as recommended)", prompt);
		Assert.Contains("(changed; recommended: A Staging first)", prompt);
		Assert.Contains("Q4 Rollback notes?: Changed notes. (changed)", prompt);

		var after = await stack.RpcAsync(browser, "attention.list", P($$"""{"nodeId":"{{seed.SupervisorId}}"}"""));
		var answered = after["items"]!.AsArray().Single(i => i!["id"]!.GetValue<string>() == itemId)!;
		Assert.Equal("answered", answered["status"]!.GetValue<string>());
		Assert.Contains("Q2 Which order?: B Production first", answered["answer"]!.GetValue<string>());
	}

	[Fact]
	public async Task F12_Questionnaire_Rejects_An_Answer_That_Does_Not_Fit()
	{
		await using var stack = await WebStack.StartAsync();
		var browser = await stack.LoginAsync();
		var seed = stack.Seed;
		var node = stack.Daemon.Registry.FindModelByNodeId(seed.SupervisorId)!;
		var itemId = Assert.Single(stack.Daemon.Attention.Post(node, [new AttentionPostRequestModel { Kind = AttentionKind.Ask, Title = Title, Questions = Questions() }])).Item.Id;

		var response = await stack.RpcRawAsync(browser, "attention.reply",
			P($$"""{"nodeId":"{{seed.SupervisorId}}","answers":[{"itemId":"{{itemId}}","text":"","questions":[{"number":2,"value":"Nowhere"}]}]}"""));
		Assert.Equal(-32602, WebStack.ErrorCode(response));
		Assert.Equal("open", stack.Daemon.Attention.GetItems(seed.SupervisorId).Single(i => i.Id == itemId).Status.ToString().ToLowerInvariant());
	}
}
