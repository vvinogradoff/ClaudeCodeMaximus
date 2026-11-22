using System.Text.Json.Serialization;

namespace TessynDesktop.Codex.Protocol;

/// <summary>Result of <c>account/read</c>. A signed-out home answers <c>account: null</c> with <c>requiresOpenaiAuth: true</c> (FR.33.13).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexGetAccountResult
{
	public CodexAccount? Account { get; set; }
	public string? UserId { get; set; }
	public bool RequiresOpenaiAuth { get; set; }

	/// <summary>True when an account is signed in; a signed-out profile is never a catalog source (FR.33.2).</summary>
	[JsonIgnore]
	public bool IsSignedIn => Account is not null;
}
