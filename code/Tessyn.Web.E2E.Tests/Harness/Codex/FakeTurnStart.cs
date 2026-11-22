using System.Text.Json;

// Copied from TessynDesktop.Tests/Codex/Fakes for the Tessyn Web E2E harness (FR.33, TC.CX.42): this project cannot
// reference the test project, so the in-process fake Codex app-server is compiled here too. Keep the two copies in step.
namespace Tessyn.Web.E2E.Tests.Harness.Codex;

/// <summary>The <c>turn/start</c> a turn script is built for: the thread, the turn id the fake gave it and the params Tessyn sent.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed record FakeTurnStart(string ThreadId, string TurnId, JsonElement Params);
