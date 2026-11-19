using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// <c>file.preview</c> with <c>itemId</c> (FR.23.30, tdcp-spec 10.A11, TC.FX.30 and TC.FX.31): a path written in a Focus item's title or body gets a handle;
/// a path that is not in the item, only in the user's answer, or that leaves the working directory is refused.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class FocusItemPreviewTests : IDisposable
{
	private readonly FocusRefsEnv _env = new();

	public void Dispose() => _env.Dispose();

	private string Params(string? itemId, string? entryId, string path)
	{
		var node = new JsonObject { ["nodeId"] = _env.Session.NodeId, ["path"] = path };
		if (itemId != null)
			node["itemId"] = itemId;
		if (entryId != null)
			node["entryId"] = entryId;
		return node.ToJsonString();
	}

	private async Task<string> PostAsync(string title, string body)
	{
		await _env.PostAsync(new JsonObject { ["kind"] = "ask", ["title"] = title, ["body"] = body });
		return _env.Attention.GetItems(_env.Session.NodeId).Single(i => i.Title == title).Id;
	}

	[Fact]
	public async Task TC_FX_30_A_path_in_the_body_gets_a_handle_that_file_read_accepts()
	{
		var full = _env.WriteFile("out/result.md", "# Result");
		var id = await PostAsync("Report", $"I wrote the report to {full} just now.");

		var preview = await _env.ResultAsync("file.preview", Params(id, null, full), TdcpTransport.Loopback);
		var read = await _env.ResultAsync("file.read", new JsonObject { ["handle"] = preview["handle"]!.GetValue<string>() }.ToJsonString(), TdcpTransport.Loopback);

		Assert.Equal("result.md", preview["name"]!.GetValue<string>());
		Assert.Equal("# Result", read["data"]!.GetValue<string>());
	}

	[Fact]
	public async Task TC_FX_30_A_path_in_the_title_gets_a_handle_and_either_slash_in_any_case_matches()
	{
		var full = _env.WriteFile("Out/Title.md", "x");
		var id = await PostAsync("Open " + full.Replace('\\', '/').ToUpperInvariant(), "no path here");

		var preview = await _env.ResultAsync("file.preview", Params(id, null, full), TdcpTransport.Loopback);

		Assert.Equal("Title.md", preview["name"]!.GetValue<string>());
	}

	[Fact]
	public async Task TC_FX_30_A_relative_path_is_resolved_against_the_working_directory()
	{
		_env.WriteFile("docs/plan.md", "x");
		var id = await PostAsync("Plan", "see docs/plan.md for the plan");

		var preview = await _env.ResultAsync("file.preview", Params(id, null, "docs/plan.md"), TdcpTransport.Loopback);

		Assert.Equal("plan.md", preview["name"]!.GetValue<string>());
	}

	[Fact]
	public async Task TC_FX_30_A_closed_item_still_answers()
	{
		var full = _env.WriteFile("closed.md", "x");
		var id = await PostAsync("Closed one", $"see {full}");
		_env.Attention.MarkAnsweredByUser(_env.Session.NodeId, [new AttentionAnswerModel { ItemId = id, Title = "Closed one", Answer = "ok" }]);

		var preview = await _env.ResultAsync("file.preview", Params(id, null, full), TdcpTransport.Loopback);

		Assert.Equal("closed.md", preview["name"]!.GetValue<string>());
	}

	[Fact]
	public async Task TC_FX_31_A_path_that_is_not_in_the_item_is_refused_even_for_an_existing_file()
	{
		var other = _env.WriteFile("other.md", "x");
		var id = await PostAsync("No path", "nothing to see");

		var loopback = await _env.ErrorAsync("file.preview", Params(id, null, other), TdcpTransport.Loopback);
		var relay = await _env.ErrorAsync("file.preview", Params(id, null, other), TdcpTransport.Relay);

		Assert.Contains("does not occur", loopback);
		Assert.Contains("does not occur", relay);
	}

	[Fact]
	public async Task TC_FX_31_A_path_only_in_the_users_answer_is_refused()
	{
		var secret = _env.WriteFile("only-in-answer.md", "x");
		var id = await PostAsync("Question", "plain text");
		_env.Attention.MarkAnsweredByUser(_env.Session.NodeId, [new AttentionAnswerModel { ItemId = id, Title = "Question", Answer = "please open " + secret }]);

		var error = await _env.ErrorAsync("file.preview", Params(id, null, secret), TdcpTransport.Loopback);

		Assert.Contains("does not occur", error);
	}

	[Fact]
	public async Task TC_FX_31_A_relative_path_that_leaves_the_working_directory_is_refused()
	{
		var id = await PostAsync("Escape", @"see ..\..\outside.txt for it");

		var error = await _env.ErrorAsync("file.preview", Params(id, null, @"..\..\outside.txt"), TdcpTransport.Loopback);

		Assert.Contains("leaves the working directory", error);
	}

	[Fact]
	public async Task TC_FX_31_An_unknown_item_is_refused()
	{
		var full = _env.WriteFile("x.md", "x");
		await PostAsync("Real", $"see {full}");

		var error = await _env.ErrorAsync("file.preview", Params("A99", null, full), TdcpTransport.Loopback);

		Assert.Contains("No such Focus item", error);
	}

	[Fact]
	public async Task TC_FX_31_Both_or_neither_of_itemId_and_entryId_is_an_invalid_params_error()
	{
		var full = _env.WriteFile("y.md", "y");
		var id = await PostAsync("Both", $"see {full}");

		var both = await _env.ErrorAsync("file.preview", Params(id, "f1:0", full), TdcpTransport.Loopback);
		var neither = await _env.ErrorAsync("file.preview", Params(null, null, full), TdcpTransport.Loopback);

		Assert.Contains("exactly one", both);
		Assert.Contains("exactly one", neither);
	}

	[Fact]
	public async Task TC_FX_31_The_relay_policy_still_applies()
	{
		_env.Base.Settings.Settings.FileAccess.Mode = FileAccessMode.ReferencedOnly;
		var plain = _env.WriteFile("plain.md", "plain");
		var id = await PostAsync("Relay", $"see {plain}");

		var denied = await _env.ErrorAsync("file.preview", Params(id, null, plain), TdcpTransport.Relay);
		var loopback = await _env.ResultAsync("file.preview", Params(id, null, plain), TdcpTransport.Loopback);

		Assert.Contains("Only files a Focus item references", denied);
		Assert.Equal("plain.md", loopback["name"]!.GetValue<string>());
	}
}
