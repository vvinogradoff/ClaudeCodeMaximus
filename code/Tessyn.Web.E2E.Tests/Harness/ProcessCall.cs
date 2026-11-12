namespace Tessyn.Web.E2E.Tests.Harness;

/// <summary>One recorded launch of the fake claude process.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record ProcessCall(string Prompt, string? Model, string? Effort, string? ProfileConfigDir, string? ResumeSessionId);
