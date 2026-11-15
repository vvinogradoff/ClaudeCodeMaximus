using System.Runtime.CompilerServices;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// Fails the whole suite fast when it was started with a plain <c>dotnet test</c>. These tests start the real WPF app many times;
/// started by hand, in parallel with other workers, they take the user's foreground and keyboard focus. The supported entry point is
/// <c>tools\e2e\Run-WpfE2E.ps1</c> (one run at a time machine-wide, guaranteed cleanup). CI or a deliberate manual run sets
/// <c>TESSYN_E2E_ALLOW_DIRECT=1</c> to skip the check.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class RunnerGuard
{
	public const string RunnerVariable = "TESSYN_E2E_RUNNER";
	public const string OverrideVariable = "TESSYN_E2E_ALLOW_DIRECT";

	/// <summary>Runs when the test assembly loads, before test discovery.</summary>
	[ModuleInitializer]
	public static void Require()
	{
		if (IsSet(RunnerVariable) || IsSet(OverrideVariable))
			return;

		throw new InvalidOperationException(
			"The WPF E2E tests must be started through the runner so they do not disturb the desktop (focus, taskbar, popups) and do not " +
			"overlap with other runs:  tools\\e2e\\Run-WpfE2E.ps1 --no-build [--filter ...]. " +
			$"CI or a deliberate manual run: set {OverrideVariable}=1.");
	}

	private static bool IsSet(string name) => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name));
}
