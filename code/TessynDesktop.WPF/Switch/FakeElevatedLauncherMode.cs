namespace TessynDesktop.WPF.Switch;

/// <summary>How <see cref="FakeElevatedRunLauncher"/> answers (<c>--elevated-launcher=fake:&lt;mode&gt;</c>, only with <c>--test-window</c>).</summary>
/// <remarks>Created by Claude</remarks>
public enum FakeElevatedLauncherMode
{
	/// <summary>Starts the launch description unelevated: the run really happens, without a prompt.</summary>
	Accept,

	/// <summary>Behaves like a declined Windows prompt.</summary>
	Decline,

	/// <summary>Behaves like a start that failed.</summary>
	Fail,

	/// <summary>Never answers: the prompt stays open.</summary>
	Hang,
}
