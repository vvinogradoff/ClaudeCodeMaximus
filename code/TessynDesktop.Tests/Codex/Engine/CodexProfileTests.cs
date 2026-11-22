using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Codex;
using TessynDesktop.Models;
using Xunit;

namespace TessynDesktop.Tests.Codex.Engine;

/// <summary>
/// ChatGPT profiles (FR.33.5): sign-in (TC.CX.37), delete (TC.CX.38), sign-out, the auth-error state (FR.33.13), pins and the inherit rule.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexProfileTests : IAsyncLifetime
{
	private readonly CodexServicesFixture _f = new();

	public Task InitializeAsync() => Task.CompletedTask;

	public async Task DisposeAsync() => await _f.DisposeAsync();

	private string Home(string id) => Path.Combine(_f.StoredRoot, id);

	private Task CompleteLoginAsync(string profileId, string loginId, bool success, string? error = null) =>
		_f.ServerOf(Home(profileId)).NotifyAsync("account/login/completed", new JsonObject { ["loginId"] = loginId, ["success"] = success, ["error"] = error });

	[Fact]
	public async Task TC_CX_37_a_browser_sign_in_returns_the_authUrl_and_adds_the_profile_only_when_it_completed()
	{
		_f.AddStoredProfile("codex_1", "a@example.com");
		_f.AddStoredProfile("codex_2", "b@example.com");
		string? signedIn = null;
		_f.Profiles.SignedIn += id => signedIn = id;

		var started = await _f.Profiles.BeginSignInAsync(deviceCode: false);

		Assert.Equal("codex_3", started.ProfileId);
		Assert.False(started.IsDeviceCode);
		Assert.Equal("https://auth.openai.com/oauth/authorize?fake=1", started.AuthUrl);
		Assert.DoesNotContain(_f.Settings.Settings.CodexProfiles, p => p.ProfileId == "codex_3");
		Assert.True(Directory.Exists(Home("codex_3")));
		var launch = _f.Launcher.Launches.Single();
		Assert.Equal(Home("codex_3"), launch.CodexHome);
		Assert.Contains("cli_auth_credentials_store=\"file\"", launch.Arguments);
		Assert.Equal("chatgpt", _f.Requests("account/login/start").Single().GetProperty("type").GetString());

		await CompleteLoginAsync("codex_3", started.LoginId, success: true);
		var outcome = await _f.Profiles.WaitForSignInAsync(started.LoginId).WaitAsync(TimeSpan.FromSeconds(10));

		Assert.Equal(new CodexSignInOutcomeModel(true, "codex_3", "u@example.com", "plus", null), outcome);
		var stored = _f.Settings.Settings.CodexProfiles.Single(p => p.ProfileId == "codex_3");
		Assert.Equal(("u@example.com", "plus"), (stored.Email, stored.PlanType));
		Assert.Equal("ChatGPT · u@example.com", _f.Profiles.DisplayNameOf("codex_3"));
		await _f.Eventually.TrueAsync(() => signedIn == "codex_3", "SignedIn was raised");
		Assert.Contains(_f.Profiles.List(), p => p.ProfileId == "codex_3" && p.IsKnownSignedIn && p.PlanType == "plus");
	}

	[Fact]
	public async Task TC_CX_37_a_failed_or_cancelled_sign_in_adds_nothing()
	{
		var failed = await _f.Profiles.BeginSignInAsync(deviceCode: false);
		await CompleteLoginAsync(failed.ProfileId, failed.LoginId, success: false, error: "access denied");
		var failure = await _f.Profiles.WaitForSignInAsync(failed.LoginId).WaitAsync(TimeSpan.FromSeconds(10));

		var cancelled = await _f.Profiles.BeginSignInAsync(deviceCode: false);
		await _f.Profiles.CancelSignInAsync(cancelled.LoginId);
		var cancel = await _f.Profiles.WaitForSignInAsync(cancelled.LoginId).WaitAsync(TimeSpan.FromSeconds(10));

		Assert.False(failure.Success);
		Assert.Equal("ChatGPT sign-in failed: access denied", failure.Error);
		Assert.Empty(_f.Settings.Settings.CodexProfiles);
		Assert.Single(_f.Requests("account/login/cancel"));
		Assert.Equal((false, "ChatGPT sign-in was cancelled."), (cancel.Success, cancel.Error));
		Assert.Equal("codex_1", cancelled.ProfileId); // the folder of the failed sign-in is reused
	}

	[Fact]
	public async Task TC_CX_37_a_device_code_sign_in_shows_the_code_and_the_URL()
	{
		var started = await _f.Profiles.BeginSignInAsync(deviceCode: true);

		Assert.True(started.IsDeviceCode);
		Assert.Equal(("ABCD-1234", "https://auth.openai.com/codex/device"), (started.UserCode, started.VerificationUrl));
		Assert.Null(started.AuthUrl);
		Assert.Equal("chatgptDeviceCode", _f.Requests("account/login/start").Single().GetProperty("type").GetString());
	}

	[Fact]
	public async Task Sign_in_again_signs_in_the_existing_profile()
	{
		_f.AddStoredProfile("codex_1", "old@example.com");

		var started = await _f.Profiles.BeginSignInAsync(deviceCode: false, profileId: "codex_1");
		await CompleteLoginAsync("codex_1", started.LoginId, success: true);
		await _f.Profiles.WaitForSignInAsync(started.LoginId).WaitAsync(TimeSpan.FromSeconds(10));

		Assert.Equal("u@example.com", _f.Settings.Settings.CodexProfiles.Single(p => p.ProfileId == "codex_1").Email);
	}

	[Fact]
	public async Task TC_CX_38_delete_stops_the_profile_s_host_and_renames_its_folder_with_rem_deleting_nothing()
	{
		_f.AddStoredProfile("codex_2", "b@example.com");
		File.WriteAllText(Path.Combine(Home("codex_2"), "auth.json"), "{}");
		await _f.Profiles.ReadAccountAsync("codex_2");
		var server = _f.ServerOf(Home("codex_2"));

		await _f.Profiles.DeleteAsync("codex_2");

		await _f.Eventually.TrueAsync(() => server.Process.HasExited, "the host stopped");
		Assert.False(Directory.Exists(Home("codex_2")));
		Assert.True(File.Exists(Path.Combine(Home("codex_2") + "_rem", "auth.json")));
		Assert.Empty(_f.Settings.Settings.CodexProfiles);
		Assert.Null(_f.Profiles.Find("codex_2"));
		await Assert.ThrowsAsync<InvalidOperationException>(() => _f.Profiles.DeleteAsync(Constants.Codex.Profiles.DefaultProfileId));
	}

	[Fact]
	public async Task Sign_out_calls_account_logout_and_keeps_the_profile_listed_signed_out()
	{
		_f.AddStoredProfile("codex_1", "a@example.com");

		await _f.Profiles.SignOutAsync("codex_1");

		Assert.Single(_f.Requests("account/logout"));
		var info = _f.Profiles.List().Single(p => p.ProfileId == "codex_1");
		Assert.False(info.IsKnownSignedIn);
	}

	[Fact]
	public async Task FR_33_13_account_updated_with_no_auth_mode_sets_the_profile_s_auth_error_and_workspace_routing_does_not()
	{
		_f.AddStoredProfile("codex_1", "a@example.com");
		await _f.Profiles.ReadAccountAsync("codex_1");
		var server = _f.ServerOf(Home("codex_1"));

		await server.NotifyAsync("account/updated", new JsonObject { ["authMode"] = "chatgpt", ["planType"] = "plus", ["reason"] = "workspaceRoutingReady" });
		await Task.Delay(100);
		Assert.False(_f.Profiles.HasAuthError("codex_1"));

		await server.NotifyAsync("account/updated", new JsonObject { ["authMode"] = null, ["planType"] = null, ["reason"] = null });
		await _f.Eventually.TrueAsync(() => _f.Profiles.HasAuthError("codex_1"), "the sign-out set the auth error");

		await _f.Profiles.ReadAccountAsync("codex_1"); // signed in again (the fake still answers an account)
		Assert.False(_f.Profiles.HasAuthError("codex_1"));
	}

	[Fact]
	public void Codex_Default_is_always_listed_first_and_resolves_without_a_home_override()
	{
		_f.AddStoredProfile("codex_2", "b@example.com");
		_f.AddStoredProfile("codex_1", "a@example.com");

		Assert.Equal(["codex-default", "codex_1", "codex_2"], _f.Profiles.List().Select(p => p.ProfileId));
		Assert.Equal(new CodexProfileRef("codex-default", null), _f.Profiles.Find("CODEX-DEFAULT"));
		Assert.Equal(_f.DefaultHome, _f.Profiles.HomeOf(_f.Profiles.Find("codex-default")!));
		Assert.Equal("ChatGPT · Codex Default", _f.Profiles.DisplayNameOf("codex-default"));
	}

	[Fact]
	public void A_pin_is_exact_and_a_missing_pin_is_an_error_that_names_it()
	{
		_f.AddStoredProfile("codex_1", "a@example.com");
		var pinned  = new SessionNodeModel { Name = "G", FileName = "g.txt", ProfileId = "codex_1", ProfileName = "ChatGPT · a@example.com" };
		var missing = new SessionNodeModel { Name = "H", FileName = "h.txt", ProfileId = "codex_7", ProfileName = "ChatGPT · gone@example.com" };

		Assert.Equal("codex_1", _f.Resolver.ResolveForTurn(pinned).Profile!.ProfileId);
		var error = _f.Resolver.ResolveForTurn(missing);
		Assert.False(error.IsResolved);
		Assert.Equal("Session 'H' is pinned to ChatGPT profile 'ChatGPT · gone@example.com', which no longer exists. It does not fall back to another profile.", error.Error);
	}

	[Fact]
	public void The_inherit_rule_takes_the_creator_s_ChatGPT_profile_then_the_directory_s_then_the_catalog_s_and_never_a_Claude_one()
	{
		_f.AddStoredProfile("codex_1", "a@example.com");
		_f.AddStoredProfile("codex_2", "b@example.com");
		_f.Settings.Settings.CodexCatalogProfileId = "codex_1";
		var dir = new DirectoryNodeModel { Path = @"C:\Proj\Alpha" };
		_f.Settings.Settings.Tree.Add(dir);
		var claudeCreator = new SessionNodeModel { Name = "Sup", FileName = "sup.txt", WorkingDirectory = dir.Path, ProfileId = "profile_1", ProfileName = "Account A" };
		var gptCreator    = new SessionNodeModel { Name = "G", FileName = "g.txt", WorkingDirectory = dir.Path, ProfileId = "codex_2", ProfileName = "ChatGPT · b@example.com" };

		Assert.Equal("codex_1", _f.Resolver.Inherit(claudeCreator, dir.Path).Profile!.ProfileId); // catalog profile
		dir.Sessions.Add(gptCreator);
		Assert.Equal("codex_2", _f.Resolver.Inherit(claudeCreator, dir.Path).Profile!.ProfileId); // the directory's last ChatGPT profile
		Assert.Equal("codex_2", _f.Resolver.Inherit(gptCreator, @"C:\Elsewhere").Profile!.ProfileId); // the creator's
	}

	[Fact]
	public void With_no_signed_in_ChatGPT_profile_the_inherit_rule_is_the_FR_33_5_error()
	{
		var choice = _f.Resolver.Inherit(null, @"C:\Proj\Alpha");

		Assert.False(choice.IsResolved);
		Assert.Equal("No signed-in ChatGPT profile. Sign in under Settings > Profiles.", choice.Error);
	}
}
