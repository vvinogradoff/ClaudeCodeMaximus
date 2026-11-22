// Copied from TessynDesktop.Tests/Codex/Fakes for the Tessyn Web E2E harness (FR.33, TC.CX.42): this project cannot
// reference the test project, so the in-process fake Codex app-server is compiled here too. Keep the two copies in step.
namespace Tessyn.Web.E2E.Tests.Harness.Codex;

/// <summary>What a <see cref="FakeCodexStep"/> does.</summary>
/// <remarks>Created by Claude</remarks>
internal enum FakeCodexStepKind
{
	Notify,
	ServerRequest,
	Crash,
	Hang,
}
