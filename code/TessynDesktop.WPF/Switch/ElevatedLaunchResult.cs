namespace TessynDesktop.WPF.Switch;

/// <summary>The result of <see cref="IElevatedRunLauncher.LaunchAsync"/>: the outcome and, for <see cref="ElevatedLaunchOutcome.Failed"/>, the reason.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record ElevatedLaunchResult(ElevatedLaunchOutcome Outcome, string? Reason = null)
{
	public static ElevatedLaunchResult Started { get; } = new(ElevatedLaunchOutcome.Started);

	public static ElevatedLaunchResult Declined { get; } = new(ElevatedLaunchOutcome.Declined);

	public static ElevatedLaunchResult Failed(string reason) => new(ElevatedLaunchOutcome.Failed, reason);
}
