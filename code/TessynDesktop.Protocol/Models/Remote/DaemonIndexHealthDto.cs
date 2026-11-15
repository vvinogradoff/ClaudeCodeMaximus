using System;
using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>The session index (search and import titles) as <c>daemon.health</c> reports it.</summary>
/// <param name="State">"idle" or "scanning".</param>
/// <param name="LastReindexUtc">When the last scan ended; null before the first one in this run.</param>
/// <param name="Queued">Scans waiting behind the running one.</param>
/// <remarks>Created by Claude</remarks>
public sealed record DaemonIndexHealthDto(string State, DateTimeOffset? LastReindexUtc, int Queued);
