namespace Tessyn.Web.Link;

/// <remarks>Created by Claude</remarks>
public sealed record BufferedEvent(long Seq, DateTimeOffset At, string Data);
