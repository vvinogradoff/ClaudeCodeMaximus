using System.Text.Json.Nodes;

// Copied from TessynDesktop.Tests/Codex/Fakes for the Tessyn Web E2E harness (FR.33, TC.CX.42): this project cannot
// reference the test project, so the in-process fake Codex app-server is compiled here too. Keep the two copies in step.
namespace Tessyn.Web.E2E.Tests.Harness.Codex;

/// <summary>
/// One step of a scripted exchange the fake app-server plays after answering <c>turn/start</c> or <c>thread/compact/start</c>:
/// a notification, a server request (the fake waits for Tessyn's answer and records it), a crash (the stream ends with an exit code and
/// stderr lines), or a hang (nothing more is sent until the turn is interrupted).
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed record FakeCodexStep(FakeCodexStepKind Kind, string Method = "", JsonObject? Params = null, int ExitCode = 0, string[]? Stderr = null)
{
	public static FakeCodexStep Notify(string method, JsonObject parameters) => new(FakeCodexStepKind.Notify, method, parameters);

	public static FakeCodexStep Request(string method, JsonObject parameters) => new(FakeCodexStepKind.ServerRequest, method, parameters);

	public static FakeCodexStep Crash(int exitCode, params string[] stderr) => new(FakeCodexStepKind.Crash, ExitCode: exitCode, Stderr: stderr);

	public static FakeCodexStep Hang() => new(FakeCodexStepKind.Hang);
}
