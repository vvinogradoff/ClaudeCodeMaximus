using System.Text.Json.Nodes;

namespace TessynDesktop.Tests.Codex.Fakes;

/// <summary>
/// One step of a scripted exchange the fake app-server plays after answering <c>turn/start</c> or <c>thread/compact/start</c>:
/// a notification, a server request (the fake waits for Tessyn's answer and records it), a crash (the stream ends with an exit code and
/// stderr lines), a hang (nothing more is sent until the turn is interrupted), or a delay (a pause before the next step).
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed record FakeCodexStep(FakeCodexStepKind Kind, string Method = "", JsonObject? Params = null, int ExitCode = 0, string[]? Stderr = null, int DelayMs = 0)
{
	public static FakeCodexStep Notify(string method, JsonObject parameters) => new(FakeCodexStepKind.Notify, method, parameters);

	public static FakeCodexStep Request(string method, JsonObject parameters) => new(FakeCodexStepKind.ServerRequest, method, parameters);

	public static FakeCodexStep Crash(int exitCode, params string[] stderr) => new(FakeCodexStepKind.Crash, ExitCode: exitCode, Stderr: stderr);

	public static FakeCodexStep Hang() => new(FakeCodexStepKind.Hang);

	/// <summary>Waits <paramref name="milliseconds"/> before the next step (an interrupt still ends it at once).</summary>
	public static FakeCodexStep Delay(int milliseconds) => new(FakeCodexStepKind.Delay, DelayMs: milliseconds);
}
