using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

// Copied from TessynDesktop.Tests/Codex/Fakes for the Tessyn Web E2E harness (FR.33, TC.CX.42): this project cannot
// reference the test project, so the in-process fake Codex app-server is compiled here too. Keep the two copies in step.
namespace Tessyn.Web.E2E.Tests.Harness.Codex;

/// <summary>A request Tessyn sent to the fake app-server, with the means to answer it.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class FakeCodexCall
{
	private readonly FakeCodexAppServer _server;

	public FakeCodexCall(FakeCodexAppServer server, string rawId, string method, JsonElement parameters)
	{
		_server = server;
		RawId   = rawId;
		Method  = method;
		Params  = parameters;
	}

	public string      RawId  { get; }
	public string      Method { get; }
	public JsonElement Params { get; }

	public string? ParamString(string name) =>
		Params.ValueKind == JsonValueKind.Object && Params.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

	public Task ReplyAsync(JsonNode result) => _server.WriteLineAsync($"{{\"id\":{RawId},\"result\":{result.ToJsonString()}}}");

	public Task ReplyEmptyAsync() => ReplyAsync(new JsonObject());

	public Task ErrorAsync(int code, string message) =>
		_server.WriteLineAsync($"{{\"error\":{{\"code\":{code},\"message\":{JsonSerializer.Serialize(message)}}},\"id\":{RawId}}}");
}
