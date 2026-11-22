namespace TessynDesktop.Codex;

/// <summary>
/// The ChatGPT profile a Codex turn or a new Codex session runs on (FR.33.5), or why there is none. <see cref="DisplayName"/> is
/// <c>ChatGPT · &lt;email&gt;</c>, stamped on the USER entry and shown in the lists.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed record CodexProfileChoice(CodexProfileRef? Profile, string DisplayName, string? Error)
{
	public bool IsResolved => Profile is not null && Error is null;
}
