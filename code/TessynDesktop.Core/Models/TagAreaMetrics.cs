namespace TessynDesktop.Models;

/// <summary>
/// What the tag area shows for the current mode (FR.24.16).
/// </summary>
/// <param name="LineHeight">Height of one line of chips; 0 when there are no chips or they are not laid out yet.</param>
/// <param name="AreaHeight">The height to give the area, or null to leave it unconstrained (no chips, or not measured yet).</param>
/// <param name="HiddenCount">Mini mode: chips below the first line; 0 in full mode.</param>
/// <param name="Scroll">Whether the area scrolls vertically (full mode).</param>
/// <remarks>Created by Claude</remarks>
public sealed record TagAreaMetrics(double LineHeight, double? AreaHeight, int HiddenCount, bool Scroll);
