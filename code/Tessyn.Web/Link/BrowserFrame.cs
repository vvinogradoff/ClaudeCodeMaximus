namespace Tessyn.Web.Link;

/// <summary>One SSE frame for a browser: optional event id plus the JSON data line.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record BrowserFrame(long? Id, string Data);
