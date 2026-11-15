using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>Makes a fake client look like a daemon that announces <c>profiles.admin</c>, as the W5 views and view models require.</summary>
/// <remarks>Created by Claude</remarks>
public static class ProfileTestSupport
{
	public static FakeTessynClient AnnouncingProfileAdmin(this FakeTessynClient client, bool announce = true)
	{
		client.ServerInfo = new HelloResult(1, "test", "machine", "run", DateTimeOffset.UtcNow, announce ? [RemoteProtocol.ProfilesAdmin.Capability] : [], 1, 0);
		return client;
	}
}
