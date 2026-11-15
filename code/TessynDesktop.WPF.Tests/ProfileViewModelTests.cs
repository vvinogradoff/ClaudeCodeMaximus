using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>The profile flyout (W5, FR.12): list, add (and login), rename, remove, re-authenticate, <c>profiles.changed</c>, and the auth banner and tree badge (ISSUE.013 R5).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ProfileViewModelTests
{
	private readonly FakeTessynClient _client = new();
	private readonly FakeDialogPresenter _presenter = new();
	private readonly ProfileViewModel _flyout;

	public ProfileViewModelTests()
	{
		_client.Profiles =
		[
			new("default", "[Sys] me@example.com", true, "me@example.com", ProfileAuthState.Unknown, true),
			new("profile_1", "Work", false, null, ProfileAuthState.LoggedIn, false),
			new("profile_2", "Spare", false, null, ProfileAuthState.LoggedOut, false),
		];
		_flyout = new ProfileViewModel(new DialogService(_presenter, new ImmediateUiDispatcher()), new ImmediateUiDispatcher(), NullLogger<ProfileViewModel>.Instance);
	}

	private void Answer(string step, string? text = null)
	{
		_presenter.Answer = dialog =>
		{
			if (step == "accept")
			{
				if (text != null)
					dialog.InputText = text;

				dialog.AcceptCommand.Execute(null);
			}
			else
			{
				dialog.CancelCommand.Execute(null);
			}

			return Task.CompletedTask;
		};
	}

	[Fact]
	public async Task Attach_ReadsTheProfiles_AndReportsTheirSignInState()
	{
		await _flyout.AttachAsync(_client);

		Assert.True(_flyout.IsAvailable);
		Assert.Equal(["[Sys] me@example.com", "Work", "Spare"], _flyout.Profiles.Select(p => p.DisplayName));
		Assert.Equal(["system account", "logged in", "logged out"], _flyout.Profiles.Select(p => p.AuthText));
		Assert.False(_flyout.Profiles[0].CanEdit);   // the Default profile is neither renamed nor removed
		Assert.True(_flyout.Profiles[1].CanEdit);
		Assert.True(_flyout.Profiles[2].IsLoggedOut);
	}

	[Fact]
	public async Task Add_RegistersTheProfile_StartsItsLogin_AndShowsItInTheList()
	{
		await _flyout.AttachAsync(_client);
		Answer("accept", "New one");

		await _flyout.AddAsync();

		Assert.Contains("profile.add:New one", _client.Calls);
		Assert.Contains(_client.Calls, c => c.StartsWith("profile.authenticate:", StringComparison.Ordinal));
		Assert.Contains(_flyout.Profiles, p => p.DisplayName == "New one");
		Assert.Contains("Login for \"New one\" started", _flyout.StatusText);
	}

	[Fact]
	public async Task Add_NeedsAName_AndCancelAddsNothing()
	{
		await _flyout.AttachAsync(_client);
		var seen = new List<string?>();
		_presenter.Answer = dialog =>
		{
			dialog.InputText = "  ";
			dialog.AcceptCommand.Execute(null);   // refused by the validator: the dialog stays open
			seen.Add(dialog.Error);
			dialog.CancelCommand.Execute(null);
			return Task.CompletedTask;
		};

		await _flyout.AddAsync();

		Assert.Equal([Constants.Profiles.NameRequired], seen);
		Assert.DoesNotContain(_client.Calls, c => c.StartsWith("profile.add", StringComparison.Ordinal));
	}

	[Fact]
	public async Task Rename_SendsTheIdAndTheNewName_AndNeverTouchesTheDefaultProfile()
	{
		await _flyout.AttachAsync(_client);
		Answer("accept", "Renamed");

		await _flyout.RenameAsync(_flyout.Profiles[0]);
		Assert.DoesNotContain(_client.Calls, c => c.StartsWith("profile.rename", StringComparison.Ordinal));

		await _flyout.RenameAsync(_flyout.Profiles[1]);
		Assert.Contains("profile.rename:profile_1:Renamed", _client.Calls);
		Assert.Contains(_flyout.Profiles, p => p.DisplayName == "Renamed");
	}

	[Fact]
	public async Task Remove_AsksFirst_ThenReportsHowManySessionsArePinned()
	{
		await _flyout.AttachAsync(_client);
		_client.RemovedProfilePinnedSessions = 4;

		Answer("cancel");
		await _flyout.RemoveAsync(_flyout.Profiles[1]);
		Assert.DoesNotContain(_client.Calls, c => c.StartsWith("profile.remove", StringComparison.Ordinal));
		Assert.True(_presenter.Shown[0].IsDestructive);

		Answer("accept");
		await _flyout.RemoveAsync(_flyout.Profiles[1]);
		Assert.Contains("profile.remove:profile_1", _client.Calls);
		Assert.DoesNotContain(_flyout.Profiles, p => p.Id == "profile_1");
		Assert.Contains("4 session(s) are pinned", _flyout.StatusText);
	}

	[Fact]
	public async Task Authenticate_StartsTheLogin_AndSaysWhenOneIsAlreadyRunning()
	{
		await _flyout.AttachAsync(_client);

		await _flyout.AuthenticateAsync(_flyout.Profiles[2]);
		Assert.Contains("profile.authenticate:profile_2", _client.Calls);
		Assert.Contains("started", _flyout.StatusText);

		_client.AuthenticateStarted = false;
		await _flyout.AuthenticateAsync(_flyout.Profiles[2]);
		Assert.Contains("already running", _flyout.StatusText);
	}

	[Fact]
	public async Task ProfilesChanged_ReloadsTheList()
	{
		await _flyout.AttachAsync(_client);
		_client.Profiles.Add(new("profile_3", "Late", false, null, ProfileAuthState.LoggedIn, false));

		_client.RaiseNotification(RemoteProtocol.ProfilesAdmin.ChangedEvent, new { });

		Assert.Contains(_flyout.Profiles, p => p.Id == "profile_3");
	}

	[Fact]
	public async Task ADaemonError_ShowsInTheStatus_AndKeepsTheList()
	{
		await _flyout.AttachAsync(_client);
		_client.ProfileException = new InvalidOperationException("nope");

		await _flyout.RefreshAsync();

		Assert.Equal("nope", _flyout.StatusText);
		Assert.Equal(3, _flyout.Profiles.Count);
	}

	private static TreeSessionDto Session(string id, string profile, bool authError = false, int workers = 0) =>
		new(id, id, workers, null, new SessionStateDto(id, false, false, null, false, null, null, false, profile, authError));

	[Fact]
	public async Task TheAuthBanner_FollowsTheSelectedSession_AndRoutesToItsProfile()
	{
		await _flyout.AttachAsync(_client);
		_client.Directories = [new TreeDirectoryDto("d1", "Repo", false, [], [Session("ok", "Work"), Session("bad", "Spare", authError: true)])];
		var tree = new TreeViewModel(_client, new ImmediateUiDispatcher(), NullLogger<TreeViewModel>.Instance, TimeSpan.Zero);
		await tree.LoadAsync();
		var banner = new AuthBannerViewModel(_flyout);
		banner.AttachTree(tree);

		tree.SelectNodeById("ok");
		Assert.False(banner.IsVisible);

		tree.SelectNodeById("bad");
		Assert.True(banner.IsVisible);
		Assert.Equal("Spare", banner.ProfileName);
		Assert.True(banner.ReauthenticateCommand.CanExecute(null));

		await banner.ReauthenticateCommand.ExecuteAsync(null);
		Assert.Contains("profile.authenticate:profile_2", _client.Calls);

		// The daemon clears the flag after the login: the banner goes away without a new selection.
		var node = tree.FindNode("bad")!;
		node.State = node.State! with { AuthError = false };
		Assert.False(banner.IsVisible);

		node.State = node.State with { AuthError = true };
		Answer("accept");
		await banner.RemoveCommand.ExecuteAsync(null);
		Assert.Contains("profile.remove:profile_2", _client.Calls);
	}

	[Fact]
	public async Task TheAuthBanner_CannotAct_OnAProfileTheListDoesNotKnow()
	{
		await _flyout.AttachAsync(_client);
		_client.Directories = [new TreeDirectoryDto("d1", "Repo", false, [], [Session("bad", "Unknown name", authError: true)])];
		var tree = new TreeViewModel(_client, new ImmediateUiDispatcher(), NullLogger<TreeViewModel>.Instance, TimeSpan.Zero);
		await tree.LoadAsync();
		var banner = new AuthBannerViewModel(_flyout);
		banner.AttachTree(tree);

		tree.SelectNodeById("bad");

		Assert.True(banner.IsVisible);
		Assert.False(banner.ReauthenticateCommand.CanExecute(null));
		Assert.False(banner.RemoveCommand.CanExecute(null));
	}

	[Fact]
	public async Task TheTreeBadge_ShowsTheProfile_OnSessionsOnly_AndWarnsWhenAWorkerRunsOnAnother()
	{
		_client.Directories = [new TreeDirectoryDto("d1", "Repo", false, [], [Session("s1", "Work", workers: 2)])];
		_client.Workers["s1"] = [Session("w1", "Work"), Session("w2", "Spare")];
		var tree = new TreeViewModel(_client, new ImmediateUiDispatcher(), NullLogger<TreeViewModel>.Instance, TimeSpan.Zero);
		await tree.LoadAsync();
		var session = tree.FindNode("s1")!;
		tree.SelectNodeById("s1");
		tree.ToggleExpandCommand.Execute(session);
		await tree.WhenIdleAsync();

		Assert.Equal("Work", session.ProfileName);
		Assert.True(session.HasProfile);
		Assert.False(tree.FindNode("d1")!.HasProfile);   // a directory has no profile
		Assert.True(session.HasProfileMismatch);
		Assert.Contains("different profile", session.ProfileToolTip);

		var changed = new List<string?>();
		session.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
		var worker = tree.FindNode("w2")!;
		worker.State = worker.State! with { ProfileName = "Work" };   // the worker was switched

		Assert.False(session.HasProfileMismatch);
		Assert.Contains(nameof(TreeNodeViewModel.HasProfileMismatch), changed);
	}
}
