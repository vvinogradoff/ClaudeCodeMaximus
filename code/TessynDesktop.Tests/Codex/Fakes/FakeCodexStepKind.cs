namespace TessynDesktop.Tests.Codex.Fakes;

/// <summary>What a <see cref="FakeCodexStep"/> does.</summary>
/// <remarks>Created by Claude</remarks>
internal enum FakeCodexStepKind
{
	Notify,
	ServerRequest,
	Crash,
	Hang,

	/// <summary>A pause before the next step, as a real model takes time between items (<see cref="FakeCodexStep.Delay"/>).</summary>
	Delay,
}
