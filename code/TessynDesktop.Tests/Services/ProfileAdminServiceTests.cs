using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// Characterisation of the profile lifecycle that lived in the Avalonia <c>ProfileController</c> (FR.12.7, FR.12.16-12.19) and now lives in
/// <see cref="ProfileAdminService"/>: every case pins what the controller did before the extraction (id choice, login, e-mail check, rename,
/// removal with directory marking and selection fix-up, what is saved), so the Avalonia dropdown behaves as it did.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ProfileAdminServiceTests : IDisposable
{
	private readonly ProfileAdminTestEnv _env = new();

	public void Dispose() => _env.Dispose();

	// ── "New..." (HandleNewProfileAsync) ─────────────────────────────────────────────────────

	[Fact]
	public async Task AddAfterLogin_LogsInUnderTheFirstFreeId_NamesTheProfileAfterTheAccount_SelectsItGloballyAndOnTheDirectory_AndSaves()
	{
		var directory = new DirectoryNodeModel { Path = @"C:\proj" };
		_env.Profiles.SetEmail(_env.ConfigDir("profile_1"), "new@example.com");

		var profile = await _env.Admin.AddAfterLoginAsync(directory);

		Assert.NotNull(profile);
		Assert.Equal(("profile_1", "new@example.com"), (profile.ProfileId, profile.DisplayName));
		Assert.Equal([("claude-test", _env.ConfigDir("profile_1"))], _env.Profiles.Logins);
		Assert.Equal(1, _env.Settings.Settings.SelectedProfileIndex);
		Assert.Equal(1, directory.SelectedProfileIndex);

		var saved = _env.Reloaded();
		Assert.Equal([("profile_1", "new@example.com")], saved.Profiles.Select(p => (p.ProfileId, p.DisplayName)));
		Assert.Equal(1, saved.SelectedProfileIndex);
	}

	[Fact]
	public async Task AddAfterLogin_SkipsIdsTakenInTheSettingsOrOnDisk()
	{
		_env.AddStored("profile_1", "one@example.com");
		Directory.CreateDirectory(_env.ConfigDir("profile_2")); // a folder without a settings entry still blocks the id
		_env.Profiles.SetEmail(_env.ConfigDir("profile_3"), "three@example.com");

		var profile = await _env.Admin.AddAfterLoginAsync(directoryToSelect: null);

		Assert.Equal("profile_3", profile!.ProfileId);
		Assert.Equal(2, _env.Settings.Settings.SelectedProfileIndex); // the new entry is the second stored one (index 0 is Default)
	}

	[Fact]
	public async Task AddAfterLogin_ACancelledLoginAddsNothingAndLeavesTheSelectionAlone()
	{
		_env.Settings.Settings.SelectedProfileIndex = 0;
		var directory = new DirectoryNodeModel { Path = @"C:\proj", SelectedProfileIndex = 0 };
		// No account answers for the new config dir: the login window was closed.

		var profile = await _env.Admin.AddAfterLoginAsync(directory);

		Assert.Null(profile);
		Assert.Empty(_env.Settings.Settings.Profiles);
		Assert.Equal(0, _env.Settings.Settings.SelectedProfileIndex);
		Assert.Equal(0, directory.SelectedProfileIndex);
	}

	[Fact]
	public async Task AddAfterLogin_TwoLoginsInFlightGetDifferentIds()
	{
		var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		_env.Profiles.OnLogin = _ => gate.Task;
		_env.Profiles.SetEmail(_env.ConfigDir("profile_1"), "a@example.com");
		_env.Profiles.SetEmail(_env.ConfigDir("profile_2"), "b@example.com");

		var first  = _env.Admin.AddAfterLoginAsync(null);
		var second = _env.Admin.AddAfterLoginAsync(null);
		gate.SetResult();
		var added = await Task.WhenAll(first, second);

		Assert.Equal(["profile_1", "profile_2"], added.Select(p => p!.ProfileId).Order());
	}

	// ── Re-authenticate (HandleReauthenticateAsync) ──────────────────────────────────────────

	[Fact]
	public async Task Authenticate_AStoredProfile_LogsInAgainstItsConfigDir_AndTakesTheDisplayNameFromTheAccount()
	{
		_env.AddStored("p1", "old@example.com");
		_env.Profiles.SetEmail(_env.ConfigDir("p1"), "other@example.com");

		var result = await _env.Admin.AuthenticateAsync("p1");

		Assert.Equal(new ProfileAuthentication(true, "other@example.com"), result);
		Assert.Equal([("claude-test", _env.ConfigDir("p1"))], _env.Profiles.Logins);
		Assert.Equal("other@example.com", _env.Reloaded().Profiles.Single().DisplayName);
	}

	[Fact]
	public async Task Authenticate_TheDefaultProfile_LogsInWithoutAConfigDir_AndChangesNoStoredName()
	{
		_env.AddStored("p1", "keep@example.com");
		_env.Profiles.SetEmail(null, "system@example.com");

		var result = await _env.Admin.AuthenticateAsync("default");

		Assert.Equal(new ProfileAuthentication(true, "system@example.com"), result);
		Assert.Equal([("claude-test", (string?)null)], _env.Profiles.Logins);
		Assert.Equal("keep@example.com", _env.Settings.Settings.Profiles.Single().DisplayName);
	}

	[Fact]
	public async Task Authenticate_ACancelledLogin_KeepsTheNameAndReportsNoAccount()
	{
		_env.AddStored("p1", "old@example.com");

		var result = await _env.Admin.AuthenticateAsync("p1");

		Assert.Equal(new ProfileAuthentication(true, null), result);
		Assert.Equal("old@example.com", _env.Settings.Settings.Profiles.Single().DisplayName);
	}

	[Fact]
	public async Task Authenticate_AnUnknownProfile_Throws()
	{
		await Assert.ThrowsAsync<ProfileAdminException>(() => _env.Admin.AuthenticateAsync("nope"));
	}

	[Fact]
	public async Task Authenticate_WhileALoginForThatProfileRuns_DoesNotStartASecondOne()
	{
		_env.AddStored("p1", "a@example.com");
		var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		_env.Profiles.OnLogin = _ => gate.Task;

		var first  = _env.Admin.AuthenticateAsync("p1");
		var second = await _env.Admin.AuthenticateAsync("p1");
		gate.SetResult();
		await first;

		Assert.Equal(new ProfileAuthentication(false, null), second);
		Assert.Single(_env.Profiles.Logins);
	}

	// ── Rename (RenameProfile) ───────────────────────────────────────────────────────────────

	[Fact]
	public void Rename_ChangesTheDisplayName_AndSaves()
	{
		_env.AddStored("p1", "old@example.com");

		var renamed = _env.Admin.Rename("p1", "Work");

		Assert.Equal("Work", renamed!.DisplayName);
		Assert.Equal("Work", _env.Reloaded().Profiles.Single().DisplayName);
	}

	[Fact]
	public void Rename_AnUnknownProfile_IsANoOp()
	{
		_env.AddStored("p1", "old@example.com");

		Assert.Null(_env.Admin.Rename("nope", "Work"));
		Assert.Equal("old@example.com", _env.Settings.Settings.Profiles.Single().DisplayName);
	}

	// ── Remove (DeleteProfile) ───────────────────────────────────────────────────────────────

	[Fact]
	public void Remove_TheSelectedProfile_FallsBackToDefault_OnTheGlobalSelectionAndTheDirectory_AndSaves()
	{
		_env.AddStored("p1", "a");
		_env.AddStored("p2", "b");
		var directory = new DirectoryNodeModel { Path = @"C:\proj", SelectedProfileIndex = 2 };

		var removal = _env.Admin.Remove("p2", selectedIndex: 2, directory);

		Assert.Equal(("p2", 2, 0, true), (removal!.Profile.ProfileId, removal.RemovedListIndex, removal.SelectedIndex, removal.WasSelected));
		Assert.Equal(0, _env.Settings.Settings.SelectedProfileIndex);
		Assert.Equal(0, directory.SelectedProfileIndex);
		var saved = _env.Reloaded();
		Assert.Equal(["p1"], saved.Profiles.Select(p => p.ProfileId));
		Assert.Equal(0, saved.SelectedProfileIndex);
		Assert.Equal(["p2"], _env.Profiles.Removed);
	}

	[Fact]
	public void Remove_AProfileBeforeTheSelectedOne_ShiftsTheSelectionDown()
	{
		_env.AddStored("p1", "a");
		_env.AddStored("p2", "b");
		_env.AddStored("p3", "c");
		var directory = new DirectoryNodeModel { Path = @"C:\proj" };

		var removal = _env.Admin.Remove("p1", selectedIndex: 3, directory);

		Assert.Equal(2, removal!.SelectedIndex);
		Assert.False(removal.WasSelected);
		Assert.Equal(2, _env.Settings.Settings.SelectedProfileIndex);
		Assert.Equal(2, directory.SelectedProfileIndex);
	}

	[Fact]
	public void Remove_AProfileAfterTheSelectedOne_KeepsTheSelection_AndWritesItBackAsTheController_DidAnyway()
	{
		_env.AddStored("p1", "a");
		_env.AddStored("p2", "b");

		var removal = _env.Admin.Remove("p2", selectedIndex: 1, directoryToSelect: null);

		Assert.Equal(1, removal!.SelectedIndex);
		Assert.Equal(1, _env.Settings.Settings.SelectedProfileIndex);
	}

	[Fact]
	public void Remove_AnUnknownProfile_IsANoOp()
	{
		_env.AddStored("p1", "a");
		_env.Settings.Settings.SelectedProfileIndex = 1;

		Assert.Null(_env.Admin.Remove("nope", 1, null));
		Assert.Single(_env.Settings.Settings.Profiles);
		Assert.Empty(_env.Profiles.Removed);
		Assert.Equal(1, _env.Settings.Settings.SelectedProfileIndex);
	}

	[Fact]
	public void Remove_WithoutASelection_UsesTheGlobalOne()
	{
		_env.AddStored("p1", "a");
		_env.AddStored("p2", "b");
		_env.Settings.Settings.SelectedProfileIndex = 2;

		var removal = _env.Admin.Remove("p2");

		Assert.True(removal!.WasSelected);
		Assert.Equal(0, _env.Settings.Settings.SelectedProfileIndex);
	}

	// ── Registering without a login (profile.add) ────────────────────────────────────────────

	[Fact]
	public void Add_RegistersAProfileUnderAFreeId_WithoutLoggingInOrChangingTheSelection()
	{
		_env.AddStored("profile_1", "a");
		_env.Settings.Settings.SelectedProfileIndex = 1;

		var profile = _env.Admin.Add("Work");

		Assert.Equal(("profile_2", "Work"), (profile.ProfileId, profile.DisplayName));
		Assert.Empty(_env.Profiles.Logins);
		Assert.Equal(1, _env.Settings.Settings.SelectedProfileIndex);
		Assert.Equal(["profile_1", "profile_2"], _env.Reloaded().Profiles.Select(p => p.ProfileId));
	}

	// ── Background start (profile.authenticate) ──────────────────────────────────────────────

	[Fact]
	public async Task TryStartAuthentication_RunsTheLoginInTheBackground_AnnouncesItAndRefusesASecondStart()
	{
		_env.AddStored("p1", "old@example.com");
		_env.Profiles.SetEmail(_env.ConfigDir("p1"), "fresh@example.com");
		var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		_env.Profiles.OnLogin = _ => gate.Task;
		var completed = new TaskCompletionSource<(string Id, bool Success)>(TaskCreationOptions.RunContinuationsAsynchronously);
		_env.Admin.AuthenticationCompleted += (id, success) => completed.TrySetResult((id, success));

		Assert.True(_env.Admin.TryStartAuthentication("p1"));
		Assert.False(_env.Admin.TryStartAuthentication("p1"));
		gate.SetResult();

		Assert.Equal(("p1", true), await completed.Task.WaitAsync(TimeSpan.FromSeconds(10)));
		Assert.Equal("fresh@example.com", _env.Settings.Settings.Profiles.Single().DisplayName);
		Assert.Single(_env.Profiles.Logins);
	}

	[Fact]
	public async Task TryStartAuthentication_ACancelledLoginCompletesAsFailed()
	{
		_env.AddStored("p1", "old@example.com");
		var completed = new TaskCompletionSource<(string Id, bool Success)>(TaskCreationOptions.RunContinuationsAsynchronously);
		_env.Admin.AuthenticationCompleted += (id, success) => completed.TrySetResult((id, success));

		_env.Admin.TryStartAuthentication("p1");

		Assert.Equal(("p1", false), await completed.Task.WaitAsync(TimeSpan.FromSeconds(10)));
	}

	[Fact]
	public void TryStartAuthentication_AnUnknownProfile_Throws()
	{
		Assert.Throws<ProfileAdminException>(() => _env.Admin.TryStartAuthentication("nope"));
	}

	// ── Auth state, events ───────────────────────────────────────────────────────────────────

	[Fact]
	public void GetAuthState_IsAFileCheck_UnknownForTheDefault_AndNullForAnUnknownId()
	{
		_env.AddStored("in", "a", loggedIn: true);
		_env.AddStored("out", "b", loggedIn: false);

		Assert.Equal(ProfileAuthState.LoggedIn, _env.Admin.GetAuthState("in"));
		Assert.Equal(ProfileAuthState.LoggedOut, _env.Admin.GetAuthState("out"));
		Assert.Equal(ProfileAuthState.Unknown, _env.Admin.GetAuthState("default"));
		Assert.Null(_env.Admin.GetAuthState("nope"));
	}

	[Fact]
	public void EveryChange_RaisesProfilesChanged_AndPublishesTheEvent()
	{
		var raised = 0;
		_env.Admin.ProfilesChanged += () => raised++;
		var published = new List<string>();
		_env.Bus.Published += n => { lock (published) published.Add(n.Method); };

		var added = _env.Admin.Add("Work");
		_env.Admin.Rename(added.ProfileId, "Home");
		_env.Admin.Remove(added.ProfileId);

		Assert.Equal(3, raised);
		Assert.Equal(["profiles.changed", "profiles.changed", "profiles.changed"], published);
	}
}
