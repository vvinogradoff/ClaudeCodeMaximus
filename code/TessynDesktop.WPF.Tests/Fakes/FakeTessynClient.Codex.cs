using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>
/// FR.33 members of <see cref="FakeTessynClient"/> (tdcp-spec 10.A14): the ChatGPT profiles that <see cref="ProfileListAsync"/> lists and the
/// sign-in, cancel and sign-out calls. Each is scriptable and records its call; <see cref="RaiseCodexLoginCompleted"/> ends a sign-in as the
/// daemon would.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class FakeTessynClient
{
	/// <summary>The ChatGPT profiles <see cref="ProfileListAsync"/> answers in <c>codexProfiles</c>; null answers none (an older daemon).</summary>
	public List<CodexProfileDto>? CodexProfiles { get; set; }

	/// <summary>What <see cref="ProfileCodexLoginAsync"/> answers; by default a browser sign-in into <c>codex_1</c>.</summary>
	public Func<string, string?, ProfileCodexLoginResult> CodexLoginAnswer { get; set; } = (mode, profileId) =>
		new ProfileCodexLoginResult(profileId ?? "codex_1", "login-1", mode,
			mode == RemoteProtocol.CodexProfilesAdmin.BrowserMode ? "https://auth.openai.com/oauth/authorize?fake=1" : null,
			mode == RemoteProtocol.CodexProfilesAdmin.DeviceCodeMode ? "https://auth.openai.com/codex/device" : null,
			mode == RemoteProtocol.CodexProfilesAdmin.DeviceCodeMode ? "ABCD-1234" : null);

	public Task<ProfileCodexLoginResult> ProfileCodexLoginAsync(string mode, string? profileId = null, CancellationToken cancellationToken = default)
	{
		Record($"profiles.codexLogin:{mode}:{profileId}");
		ThrowIfProfileFails();
		return Task.FromResult(CodexLoginAnswer(mode, profileId));
	}

	public Task<ProfileCodexLoginCancelResult> ProfileCodexLoginCancelAsync(string loginId, CancellationToken cancellationToken = default)
	{
		Record($"profiles.codexLoginCancel:{loginId}");
		ThrowIfProfileFails();
		return Task.FromResult(new ProfileCodexLoginCancelResult(true));
	}

	public Task<CodexProfileDto> ProfileCodexLogoutAsync(string profileId, CancellationToken cancellationToken = default)
	{
		Record($"profiles.codexLogout:{profileId}");
		ThrowIfProfileFails();
		var list = CodexProfiles ?? throw new InvalidOperationException("No ChatGPT profiles are scripted");
		var index = list.FindIndex(p => p.Id == profileId);
		var signedOut = list[index] with { SignedIn = false };
		list[index] = signedOut;
		return Task.FromResult(signedOut);
	}

	/// <summary>Publishes <c>profiles.codexLoginCompleted</c>; on success the profile is added to <see cref="CodexProfiles"/> (or updated) first.</summary>
	public void RaiseCodexLoginCompleted(ProfileCodexLoginCompletedPayload payload)
	{
		if (payload.Success)
		{
			CodexProfiles ??= [];
			var profile = new CodexProfileDto(payload.ProfileId, "ChatGPT · " + payload.Email, payload.Email, payload.Plan, false, true, false, false);
			var index = CodexProfiles.FindIndex(p => p.Id == payload.ProfileId);
			if (index >= 0)
				CodexProfiles[index] = profile;
			else
				CodexProfiles.Add(profile);
		}

		RaiseNotification(RemoteProtocol.CodexProfilesAdmin.LoginCompletedEvent, payload);
	}
}
