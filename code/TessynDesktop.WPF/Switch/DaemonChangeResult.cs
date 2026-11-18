namespace TessynDesktop.WPF.Switch;

/// <summary>The decision of the commit comparison with both commits and its reason, for the log (FR.32.9).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record DaemonChangeResult(DaemonChangeDecision Decision, string? RunningCommit, string? NewestCommit, string Reason);
