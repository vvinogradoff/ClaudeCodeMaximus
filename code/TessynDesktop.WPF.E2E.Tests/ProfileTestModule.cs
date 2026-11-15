using System.Text.Json.Nodes;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// A test-only (loopback-only, like the real J3 methods: <c>remoteCallable: false</c>) <c>profile.list</c>, <c>profile.authenticate</c> and <c>profile.switch</c>: two stored profiles plus the Default one, and every switch is
/// recorded (session, profile, includeDescendants) and answered from a script. The real switch service needs the profile stores and the Claude config
/// directories the E2E host does not have (its behaviour is covered by the daemon's own tests); this module checks what the WPF client sends and shows.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class ProfileTestModule : IRemoteRpcModule
{
	private readonly List<(string NodeId, string ProfileId, bool IncludeDescendants)> _switches = [];
	private readonly List<string> _authenticated = [];

	/// <summary>The answer of the next <c>profile.switch</c> calls; null answers "the session moved, nothing else".</summary>
	public Func<string, string, bool, ProfileSwitchResultDto>? SwitchAnswer { get; set; }

	public IReadOnlyList<(string NodeId, string ProfileId, bool IncludeDescendants)> Switches
	{
		get
		{
			lock (_switches)
				return _switches.ToList();
		}
	}

	public IReadOnlyList<string> Authenticated
	{
		get
		{
			lock (_authenticated)
				return _authenticated.ToList();
		}
	}

	public void Register(TdcpMethodRegistry registry)
	{
		registry.AddSync("profile.list", _ => new ProfileListResult(
		[
			new ProfileDto("default", "[Sys] test@example.com", true, "test@example.com", ProfileAuthState.Unknown, true),
			new ProfileDto("profile_1", "Work", false, null, ProfileAuthState.LoggedIn, false),
			new ProfileDto("profile_2", "Spare", false, null, ProfileAuthState.LoggedOut, false),
		]), remoteCallable: false);
		registry.AddSync("profile.authenticate", Authenticate, remoteCallable: false);
		registry.AddSync("profile.switch", Switch, remoteCallable: false);
	}

	private object? Authenticate(JsonNode? @params)
	{
		lock (_authenticated)
			_authenticated.Add(TdcpParams.RequireString(@params, "profileId"));

		return new ProfileAuthenticateResult(true);
	}

	private object? Switch(JsonNode? @params)
	{
		var nodeId = TdcpParams.RequireString(@params, "nodeId");
		var profileId = TdcpParams.RequireString(@params, "profileId");
		var include = TdcpParams.OptionalBool(@params, "includeDescendants") ?? false;
		lock (_switches)
			_switches.Add((nodeId, profileId, include));

		return SwitchAnswer?.Invoke(nodeId, profileId, include)
			?? new ProfileSwitchResultDto(profileId, profileId == "profile_1" ? "Work" : "Spare", true, [], [], [], []);
	}
}

/// <summary>
/// Stands in for the profile admin service only so that <c>client.hello</c> announces <c>profiles.admin</c> on loopback (the daemon announces it when the service is wired);
/// the <c>profile.*</c> methods themselves are the test module's. Nothing here is called.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class AnnouncingProfileAdmin : IProfileAdminService
{
	public event Action? ProfilesChanged { add { } remove { } }

	public event Action<string, bool>? AuthenticationCompleted { add { } remove { } }

	public IReadOnlyList<ClaudeProfileModel> GetProfiles() => throw new NotSupportedException();

	public int GetSelectedProfileIndex() => throw new NotSupportedException();

	public ProfileAuthState? GetAuthState(string profileId) => throw new NotSupportedException();

	public ClaudeProfileModel Add(string displayName) => throw new NotSupportedException();

	public Task<ClaudeProfileModel?> AddAfterLoginAsync(DirectoryNodeModel? directoryToSelect) => throw new NotSupportedException();

	public ClaudeProfileModel? Rename(string profileId, string displayName) => throw new NotSupportedException();

	public ProfileRemoval? Remove(string profileId, int selectedIndex, DirectoryNodeModel? directoryToSelect) => throw new NotSupportedException();

	public ProfileRemoval? Remove(string profileId) => throw new NotSupportedException();

	public Task<ProfileAuthentication> AuthenticateAsync(string profileId) => throw new NotSupportedException();

	public bool TryStartAuthentication(string profileId) => throw new NotSupportedException();
}
