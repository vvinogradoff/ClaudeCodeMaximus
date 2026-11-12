using System;

namespace TessynDesktop.Models.Remote;

/// <summary>One usage window.</summary>
/// <param name="Utilization">0-100; zero once the window has rolled over.</param>
/// <param name="ResetsAtUtc">When the window resets.</param>
/// <param name="IsStale">The window rolled over since the reading was taken.</param>
/// <remarks>Created by Claude</remarks>
public sealed record UsageWindowDto(double Utilization, DateTimeOffset ResetsAtUtc, bool IsStale);
