using System.Text.Json;

namespace TessynDesktop.Tests.Codex.Fakes;

/// <summary>A message the fake app-server received from Tessyn: a request (<see cref="Id"/> set), a notification, or an answer to a server request.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed record FakeCodexMessage(string? Method, string? Id, JsonElement Params, JsonElement Raw)
{
	public bool IsResponse => Method is null;
}
