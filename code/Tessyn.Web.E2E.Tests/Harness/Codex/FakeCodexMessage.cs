using System.Text.Json;

// Copied from TessynDesktop.Tests/Codex/Fakes for the Tessyn Web E2E harness (FR.33, TC.CX.42): this project cannot
// reference the test project, so the in-process fake Codex app-server is compiled here too. Keep the two copies in step.
namespace Tessyn.Web.E2E.Tests.Harness.Codex;

/// <summary>A message the fake app-server received from Tessyn: a request (<see cref="Id"/> set), a notification, or an answer to a server request.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed record FakeCodexMessage(string? Method, string? Id, JsonElement Params, JsonElement Raw)
{
	public bool IsResponse => Method is null;
}
