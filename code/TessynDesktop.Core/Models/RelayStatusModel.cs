using System;

namespace TessynDesktop.Models;

/// <summary>Snapshot of the relay connector's state.</summary>
/// <param name="State">Current state.</param>
/// <param name="Since">When the connector entered <paramref name="State"/>.</param>
/// <param name="LastError">Human-readable reason, only for <see cref="RelayState.Error"/>.</param>
/// <remarks>Created by Claude</remarks>
public sealed record RelayStatusModel(RelayState State, DateTimeOffset Since, string? LastError = null);
