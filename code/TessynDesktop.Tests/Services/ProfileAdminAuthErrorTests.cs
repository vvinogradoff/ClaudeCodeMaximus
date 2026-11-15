using TessynDesktop.Services;

namespace TessynDesktop.Tests.Services;

/// <summary>V5: a successful re-authentication ends the sessions' auth error; a login that did not succeed does not.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ProfileAdminAuthErrorTests : IDisposable
{
	private readonly ProfileAdminTestEnv _env = new();
	private readonly SessionAuthErrorTracker _tracker = new();
	private readonly ProfileAdminService _admin;

	public ProfileAdminAuthErrorTests()
	{
		_admin = new ProfileAdminService(_env.Profiles, _env.Settings, _env.Bus, _tracker);
		_tracker.Set("a", true);
		_tracker.Set("b", true);
	}

	public void Dispose() => _env.Dispose();

	[Fact]
	public async Task ASuccessfulAuthentication_ClearsTheAuthErrorOfEverySession()
	{
		_env.AddStored("p1", "old@example.com");
		_env.Profiles.SetEmail(_env.ConfigDir("p1"), "new@example.com");

		await _admin.AuthenticateAsync("p1");

		Assert.False(_tracker.Has("a"));
		Assert.False(_tracker.Has("b"));
	}

	[Fact]
	public async Task TheBackgroundVariant_ClearsItToo()
	{
		_env.AddStored("p1", "old@example.com");
		_env.Profiles.SetEmail(_env.ConfigDir("p1"), "new@example.com");
		var done = new TaskCompletionSource();
		_admin.AuthenticationCompleted += (_, _) => done.TrySetResult();

		Assert.True(_admin.TryStartAuthentication("p1"));
		await done.Task.WaitAsync(TimeSpan.FromSeconds(10));

		Assert.False(_tracker.Has("a"));
	}

	[Fact]
	public async Task ACancelledLogin_LeavesTheAuthError()
	{
		_env.AddStored("p1", "old@example.com"); // no account email comes back: the login was cancelled

		await _admin.AuthenticateAsync("p1");

		Assert.True(_tracker.Has("a"));
		Assert.True(_tracker.Has("b"));
	}
}
