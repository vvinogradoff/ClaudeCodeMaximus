using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Codex.Protocol;
using TessynDesktop.Models;

namespace TessynDesktop.Codex;

/// <summary>
/// The ChatGPT profiles of the Codex engine (FR.33.5): Codex Default and the stored <c>codex_N</c> homes, sign-in through the app-server,
/// sign-out, delete, and each profile's auth-error state (FR.33.13). The TDCP layer calls it in phase 5; it never opens a browser.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ICodexProfileService
{
	/// <summary>Codex Default first, then the stored profiles in id order.</summary>
	IReadOnlyList<CodexProfileInfoModel> List();

	/// <summary>The profile with this id (any case); null for an unknown id. Codex Default always exists.</summary>
	CodexProfileRef? Find(string profileId);

	/// <summary><c>ChatGPT · &lt;email&gt;</c> for a known profile, else null.</summary>
	string? DisplayNameOf(string profileId);

	/// <summary>The profile's <c>CODEX_HOME</c> folder (Codex Default: <c>%USERPROFILE%\.codex</c>).</summary>
	string HomeOf(CodexProfileRef profile);

	/// <summary><c>account/read</c> on the profile's host; records the e-mail and plan, and clears or sets the auth-error state.</summary>
	Task<CodexGetAccountResult> ReadAccountAsync(string profileId, CancellationToken cancellationToken = default);

	/// <summary>
	/// The catalog profile (FR.33.2): <c>CodexCatalogProfileId</c> when it is signed in, else Codex Default when signed in, else the first signed-in
	/// stored profile; null when none is signed in. A signed-out profile is never the catalog source.
	/// </summary>
	Task<CodexProfileRef?> ResolveCatalogProfileAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Starts a ChatGPT sign-in (<c>account/login/start</c>): into a new stored profile, or into <paramref name="profileId"/> (Sign in again).
	/// Nothing is added until <see cref="WaitForSignInAsync"/> reports success.
	/// </summary>
	Task<CodexSignInStartModel> BeginSignInAsync(bool deviceCode, string? profileId = null, CancellationToken cancellationToken = default);

	/// <summary>Waits for <c>account/login/completed</c> of the sign-in; on success the profile is added (or updated) with its e-mail and plan.</summary>
	Task<CodexSignInOutcomeModel> WaitForSignInAsync(string loginId, CancellationToken cancellationToken = default);

	/// <summary>Cancels a sign-in in progress (<c>account/login/cancel</c>); it ends as a failure that adds nothing.</summary>
	Task CancelSignInAsync(string loginId, CancellationToken cancellationToken = default);

	/// <summary><c>account/logout</c>; the profile stays listed, signed out.</summary>
	Task SignOutAsync(string profileId, CancellationToken cancellationToken = default);

	/// <summary>Stops the profile's host and renames its folder with the <c>_rem</c> suffix (nothing is deleted). Codex Default can't be deleted.</summary>
	Task DeleteAsync(string profileId, CancellationToken cancellationToken = default);

	bool HasAuthError(string profileId);

	/// <summary>Sets the profile's auth-error state (FR.33.13); raises <see cref="AuthErrorChanged"/> on a change.</summary>
	void SetAuthError(string profileId, bool hasAuthError);

	/// <summary>Raised with the profile id when its auth-error state changes.</summary>
	event Action<string, bool>? AuthErrorChanged;

	/// <summary>Raised with the profile id after a successful sign-in (the catalog refreshes on it, FR.33.2).</summary>
	event Action<string>? SignedIn;
}
