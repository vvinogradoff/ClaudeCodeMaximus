using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;

namespace TessynDesktop.Codex;

/// <summary>
/// ChatGPT profile administration for the TDCP layer (FR.33.5, FR.33.17, tdcp-spec 10.A14): sign-in with its completion event, cancel, sign-out
/// and delete, each followed by <c>profiles.changed</c>; an auth-error change of any ChatGPT profile publishes <c>profiles.changed</c> too.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ICodexProfileAdminService
{
	/// <summary>
	/// Starts a sign-in into a new <c>codex_N</c> (<paramref name="profileId"/> null) or into an existing profile (Sign in again) and answers at once;
	/// <c>profiles.codexLoginCompleted</c> follows when it ends (and <c>profiles.changed</c> on success).
	/// </summary>
	/// <exception cref="System.InvalidOperationException">An unknown <paramref name="profileId"/>.</exception>
	/// <exception cref="CodexNotFoundException">Codex is not installed or its configured path is unusable.</exception>
	Task<CodexSignInStartModel> BeginLoginAsync(bool deviceCode, string? profileId, CancellationToken cancellationToken = default);

	/// <summary>Cancels a sign-in in progress; its completion event then reports the cancel.</summary>
	/// <exception cref="System.InvalidOperationException">No sign-in with that id is in progress.</exception>
	Task CancelLoginAsync(string loginId, CancellationToken cancellationToken = default);

	/// <summary>Signs the profile out (<c>account/logout</c>); it stays listed. Answers the profile as listed afterwards.</summary>
	/// <exception cref="System.InvalidOperationException">An unknown profile.</exception>
	Task<CodexProfileInfoModel> LogoutAsync(string profileId, CancellationToken cancellationToken = default);

	/// <summary>Deletes a stored profile: its host is stopped and its folder renamed with <c>_rem</c>; nothing is deleted. Codex Default can't be removed.</summary>
	/// <exception cref="System.InvalidOperationException">An unknown profile, or Codex Default.</exception>
	Task<CodexProfileRemovalModel> RemoveAsync(string profileId, CancellationToken cancellationToken = default);
}
