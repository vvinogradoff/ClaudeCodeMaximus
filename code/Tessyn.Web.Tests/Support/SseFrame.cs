namespace Tessyn.Web.Tests.Support;

/// <remarks>Created by Claude</remarks>
public sealed record SseFrame(string? Id, string? Data, bool IsPing);
