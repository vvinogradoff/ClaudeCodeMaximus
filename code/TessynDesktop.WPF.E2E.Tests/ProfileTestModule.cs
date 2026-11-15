using System.Text.Json.Nodes;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// A test-only <c>profile.list</c>, <c>profile.authenticate</c> and <c>profile.switch</c>: two stored profiles plus the Default one, and every switch is
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
		]), remoteCallable: true);
		registry.AddSync("profile.authenticate", Authenticate, remoteCallable: true);
		registry.AddSync("profile.switch", Switch, remoteCallable: true);
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
