using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace TessynDesktop.Tests.Codex.Engine;

/// <summary>
/// The orchestration tools with GPT models (FR.33.15): spawn on a ChatGPT profile (TC.CX.25), GPT-looking ids (TC.CX.26), create_session
/// profiles, streaming and efforts (TC.CX.27, TC.CX.06), tiers through the tools (TC.CX.24), list_sessions rows (TC.CX.28), tool descriptions.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class GptOrchestrationTests : IAsyncLifetime
{
	private GptOrchestrationTestEnv _env = null!;

	public async Task InitializeAsync() => _env = await GptOrchestrationTestEnv.CreateAsync();

	public async Task DisposeAsync() => await _env.DisposeAsync();

	private static JsonObject Args(params (string Key, JsonNode? Value)[] pairs)
	{
		var args = new JsonObject();
		foreach (var (key, value) in pairs)
			args[key] = value;
		return args;
	}

	private static async Task<string> ErrorOf(Func<Task<JsonNode>> action) => (await Assert.ThrowsAnyAsync<Exception>(action)).Message;

	[Fact]
	public async Task TC_CX_25_spawn_with_a_GPT_model_makes_a_Codex_worker_on_the_directory_s_ChatGPT_profile_never_on_the_supervisor_s_Claude_one()
	{
		_env.AddGptSession("G", "codex_2");
		var sup = _env.AddClaudeSupervisor();

		await _env.Call("spawn_session", Args(("name", "W"), ("model", "gpt-x-sol")), sup);

		var worker = _env.Named("W");
		Assert.Equal(("codex", "codex_2", "ChatGPT · w@example.com", "gpt-x-sol"), (worker.EngineId, worker.ProfileId, worker.ProfileName, worker.ModelId));
	}

	[Fact]
	public async Task TC_CX_25_without_a_directory_ChatGPT_session_the_catalog_profile_is_used()
	{
		var sup = _env.AddClaudeSupervisor();

		await _env.Call("spawn_session", Args(("name", "W"), ("model", "gpt-x-sol")), sup);

		Assert.Equal("codex_1", _env.Named("W").ProfileId);
	}

	[Fact]
	public async Task TC_CX_25_with_no_signed_in_ChatGPT_profile_spawn_fails_with_the_stated_message_and_creates_nothing()
	{
		await using var env = await GptOrchestrationTestEnv.CreateAsync(signedIn: false);
		var sup = env.AddClaudeSupervisor();

		var error = await ErrorOf(() => env.Call("spawn_session", Args(("name", "W"), ("model", "gpt-x-sol")), sup));

		Assert.Equal("No signed-in ChatGPT profile. Sign in under Settings > Profiles.", error);
		Assert.DoesNotContain(env.Settings.Settings.Tree.SelectMany(d => d.Sessions), s => s.Name == "W");
	}

	[Fact]
	public async Task A_GPT_supervisor_s_Claude_worker_does_not_inherit_the_ChatGPT_pin_and_its_GPT_worker_does()
	{
		var g = _env.AddGptSession("G", "codex_2");

		await _env.Call("spawn_session", Args(("name", "Claude W"), ("model", FixedClaudeModels.Sonnet)), g);
		await _env.Call("spawn_session", Args(("name", "GPT W"), ("model", "gpt-x-luna")), g);

		var claude = _env.Named("Claude W");
		Assert.Null(claude.EngineId);
		Assert.Null(claude.ProfileId);
		Assert.Null(claude.ProfileName);
		Assert.Equal("codex_2", _env.Named("GPT W").ProfileId);
	}

	[Fact]
	public async Task TC_CX_26_GPT_looking_ids_not_in_the_catalog_are_refused_with_the_GPT_list_and_other_ids_keep_today_s_behaviour()
	{
		var sup = _env.AddClaudeSupervisor();

		var stale  = await ErrorOf(() => _env.Call("spawn_session", Args(("name", "A"), ("model", "gpt-old-1")), sup));
		var oSerie = await ErrorOf(() => _env.Call("spawn_session", Args(("name", "B"), ("model", "o9-mini")), sup));
		await _env.Call("spawn_session", Args(("name", "C"), ("model", "my-local:7b")), sup);

		Assert.Equal("'gpt-old-1' looks like a GPT model but is not in the GPT catalog. GPT models: gpt-x-sol, gpt-x-astra, gpt-x-luna.", stale);
		Assert.StartsWith("'o9-mini' looks like a GPT model", oSerie);
		Assert.Equal("my-local:7b", _env.Named("C").ModelId);
		Assert.Null(_env.Named("C").EngineId);
		var setError = await ErrorOf(() => _env.Call("set_session_model", Args(("nodeId", _env.Named("C").NodeId), ("model", "gpt-old-1")), sup));
		Assert.StartsWith("'gpt-old-1' looks like a GPT model", setError);
		var orchestrateError = await ErrorOf(() => _env.Call("orchestrate_parallel", Args(("model", "gpt-old-1"), ("tasks", new JsonArray(new JsonObject { ["prompt"] = "x" }))), sup));
		Assert.StartsWith("'gpt-old-1' looks like a GPT model", orchestrateError);
	}

	[Fact]
	public async Task TC_CX_24_through_the_tools_a_Sonnet_caller_may_spawn_sol_but_not_astra()
	{
		var sonnet = _env.AddClaudeSupervisor("Sonnet sup", FixedClaudeModels.Sonnet);

		await _env.Call("spawn_session", Args(("name", "Sol W"), ("model", "gpt-x-sol")), sonnet);
		var error = await ErrorOf(() => _env.Call("spawn_session", Args(("name", "Astra W"), ("model", "gpt-x-astra")), sonnet));

		Assert.Equal("codex", _env.Named("Sol W").EngineId);
		Assert.Equal("Tier violation: a Sonnet session cannot spawn a Opus worker. Caller model: 'claude-sonnet-5-5', requested: 'gpt-x-astra'.", error);
	}

	[Fact]
	public async Task TC_CX_27_create_session_with_a_GPT_model_and_a_Claude_profile_is_the_cross_engine_error()
	{
		var sup = _env.AddClaudeSupervisor();

		var error = await ErrorOf(() => _env.Call("create_session", Args(("name", "New"), ("model", "gpt-x-sol"), ("profile", "p1")), sup));

		Assert.Equal("Profile 'Account A' is a Claude profile; GPT models need a ChatGPT profile: ChatGPT · u@example.com, ChatGPT · w@example.com.", error);
	}

	[Fact]
	public async Task TC_CX_27_create_session_with_a_GPT_model_and_streaming_succeeds_with_streaming_n_a()
	{
		var sup = _env.AddClaudeSupervisor();

		var text = GptOrchestrationTestEnv.TextOf(await _env.Call("create_session", Args(("name", "New"), ("model", "gpt-x-sol"), ("streaming", true), ("profile", "codex_2")), sup));

		var created = _env.Named("New");
		Assert.Contains("streaming=n/a", text);
		Assert.Contains("profile=ChatGPT · w@example.com", text);
		Assert.Equal(("codex", "codex_2", false), (created.EngineId, created.ProfileId, created.IsStreamingMode));
	}

	[Fact]
	public async Task A_ChatGPT_profile_with_a_Claude_model_is_the_reverse_cross_engine_error()
	{
		var sup = _env.AddClaudeSupervisor();

		var error = await ErrorOf(() => _env.Call("create_session", Args(("name", "New"), ("model", FixedClaudeModels.Sonnet), ("profile", "codex_1")), sup));

		Assert.StartsWith("Profile 'ChatGPT · u@example.com' is a ChatGPT profile; Claude models need a Claude profile:", error);
	}

	[Fact]
	public async Task TC_CX_06_create_session_with_an_effort_the_GPT_model_lacks_lists_its_efforts()
	{
		var sup = _env.AddClaudeSupervisor();

		var error = await ErrorOf(() => _env.Call("create_session", Args(("name", "New"), ("model", "gpt-x-luna"), ("effort", "high")), sup));
		var ok    = GptOrchestrationTestEnv.TextOf(await _env.Call("create_session", Args(("name", "Fine"), ("model", "gpt-x-sol"), ("effort", "xhigh")), sup));

		Assert.Equal("Effort 'high' is not offered by gpt-x-luna. Its efforts: low, medium.", error);
		Assert.Contains("effort=xhigh", ok);
	}

	[Fact]
	public async Task TC_CX_28_list_sessions_shows_a_GPT_row_with_its_ChatGPT_profile_and_engine_and_leaves_Claude_rows_alone()
	{
		var g   = _env.AddGptSession("G", "codex_1");
		var sup = _env.AddClaudeSupervisor();

		var text  = GptOrchestrationTestEnv.TextOf(await _env.Call("list_sessions", new JsonObject(), sup));
		var lines = text.Split('\n');

		var gRow   = lines.Single(l => l.Contains(" G "));
		var supRow = lines.Single(l => l.Contains(" Sup "));
		Assert.Matches(@"  G  \S+  engine=codex  profile=ChatGPT · u@example\.com$", gRow.TrimEnd('\r'));
		Assert.DoesNotContain("no-resume", gRow);
		Assert.DoesNotContain("engine=", supRow);
		Assert.Contains("profile=Account B", supRow);
	}

	[Fact]
	public async Task The_orchestration_tool_descriptions_say_GPT_models_run_on_the_ChatGPT_plan()
	{
		var list  = await _env.Server.HandleToolCallAsync(new JsonObject { ["name"] = "list_sessions", ["arguments"] = new JsonObject() }, _env.AddClaudeSupervisor());
		Assert.NotNull(list);

		var tools = typeof(TessynDesktop.Services.AgentMcpServer).GetMethod("HandleToolsList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
			.Invoke(null, null) as JsonNode;
		foreach (var name in new[] { "spawn_session", "create_session", "set_session_model", "orchestrate_parallel", "orchestrate_pipeline" })
		{
			var description = tools!["tools"]!.AsArray().Single(t => t!["name"]!.GetValue<string>() == name)!["description"]!.GetValue<string>();
			Assert.EndsWith(Constants.Codex.Texts.GptToolDescription, description);
		}
	}
}
