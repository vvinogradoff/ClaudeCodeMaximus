namespace Tessyn.Web.Models;

/// <summary>What /healthz tells a non-loopback caller: liveness only.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record PublicHealthResponse(bool Ok);
