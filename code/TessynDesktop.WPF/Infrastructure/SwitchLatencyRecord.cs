namespace TessynDesktop.WPF.Infrastructure;

/// <summary>One measured session switch.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record SwitchLatencyRecord(string NodeId, double ClickToPaintMilliseconds, double PageMilliseconds, bool Warm);
