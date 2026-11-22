namespace TessynDesktop.Models;

/// <summary>
/// A ChatGPT profile of the Codex engine (FR.33.5): one <c>CODEX_HOME</c> with its own ChatGPT login. Stored profiles have ids <c>codex_1</c>,
/// <c>codex_2</c> … under <c>%AppData%\TessynDesktop\codex-profiles\</c>; <c>codex-default</c> is the User's own <c>%USERPROFILE%\.codex</c>,
/// kept here only once its account has been read.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexProfileModel
{
	public string ProfileId { get; set; } = string.Empty;

	/// <summary>The ChatGPT account's e-mail, from <c>account/read</c>.</summary>
	public string Email { get; set; } = string.Empty;

	/// <summary>The plan type Codex reports (<c>plus</c>, <c>pro</c>, …).</summary>
	public string? PlanType { get; set; }
}
