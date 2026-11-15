using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>Fixes of the W5 review (446): the badge follows the pin (H1), the cascade count is honest (M1), the parent is notified only on a profile change (M2), L1 to L5.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ProfileReviewFixTests
{
	private readonly FakeTessynClient _client = new FakeTessynClient().AnnouncingProfileAdmin();
	private readonly FakeDialogPresenter _presenter = new();
	private readonly TreeContext _context = new();

	public ProfileReviewFixTests()
	{
		_client.Profiles =
		[
			new("default", "[Sys] me@example.com", true, "me@example.com", ProfileAuthState.Unknown, true),
			new("profile_1", "Work", false, null, ProfileAuthState.LoggedIn, false),
		];
	}

	private DialogService Dialogs => new(_presenter, new ImmediateUiDispatcher());

	private ProfileSwitchViewModel NewSwitch(int max = ProfileSwitchViewModel.DefaultMaxDescendants)
	{
		var vm = new ProfileSwitchViewModel(Dialogs, _context, new ImmediateUiDispatcher(), NullLogger<ProfileSwitchViewModel>.Instance, max);
		vm.Attach(_client);
		return vm;
	}

	private ProfileViewModel NewFlyout() => new(Dialogs, new ImmediateUiDispatcher(), NullLogger<ProfileViewModel>.Instance);

	private static SessionStateDto State(string id, string? stamped, string? pinned = null, string? switching = null, bool running = false) =>
		new(id, running, false, null, false, null, null, false, stamped, PinnedProfileId: pinned == null ? null : "id-" + pinned, PinnedProfileName: pinned, SwitchingToProfileName: switching);

	private static TreeSessionDto Session(string id, SessionStateDto state, int workers = 0, string? supervisor = null) => new(id, id, workers, supervisor, state);

	private async Task<TreeViewModel> TreeAsync(params TreeSessionDto[] top)
	{
		_client.Directories = [new TreeDirectoryDto("d1", "Repo", false, [], top)];
		var tree = new TreeViewModel(_client, new ImmediateUiDispatcher(), NullLogger<TreeViewModel>.Instance, TimeSpan.Zero);
		_context.Current = tree;
		await tree.LoadAsync();
		return tree;
	}

	private void Script(params string[] answers)
	{
		var queue = new Queue<string>(answers);
		_presenter.Answer = dialog =>
		{
			var step = queue.Count > 0 ? queue.Dequeue() : "cancel";
			if (step == "accept")
				dialog.AcceptCommand.Execute(null);
			else if (step == "cancel")
				dialog.CancelCommand.Execute(null);
			else
				dialog.ChooseCommand.Execute(dialog.Choices.Single(c => c.Id == step));
			return Task.CompletedTask;
		};
	}

	// ---- H1 -----------------------------------------------------------------------------------------------------------------------

	[Fact]
	public async Task H1_TheBadgeShowsThePin_NotTheProfileOfTheLastUserEntry()
	{
		var tree = await TreeAsync(Session("s1", State("s1", stamped: "Old", pinned: "Work")), Session("s2", State("s2", stamped: "Old")));

		Assert.Equal("Work", tree.FindNode("s1")!.ProfileName);
		Assert.Equal("Old", tree.FindNode("s2")!.ProfileName);   // no pin (older daemon, or never run): the stamped profile
	}

	[Fact]
	public async Task H1_ADaemonStateEventAfterASwitch_KeepsTheNewBadge_AndAQueuedSwitchShowsSwitchingTo()
	{
		var tree = await TreeAsync(Session("s1", State("s1", stamped: "Old", pinned: "Old")), Session("s2", State("s2", stamped: "Old", pinned: "Old", running: true)));
		var switcher = NewSwitch();
		_client.ProfileSwitchResult = new ProfileSwitchResultDto("profile_1", "Work", NodeMoved: true, [], [], [], []);
		Script("profile_1");
		var node = tree.FindNode("s1")!;

		await switcher.SwitchAsync(node);
		Assert.Equal("Work", node.ProfileName);

		// The daemon's next state: the pin is Work, but the last USER entry still carries the old name. The badge must not fall back to it.
		_client.RaiseNotification(RemoteProtocol.Events.SessionChanged, new SessionChangedPayload(State("s1", stamped: "Old", pinned: "Work")));
		Assert.Equal("Work", node.ProfileName);

		// A queued switch (the session is running) shows where it is going, and its tooltip says so.
		var running = tree.FindNode("s2")!;
		_client.RaiseNotification(RemoteProtocol.Events.SessionChanged, new SessionChangedPayload(State("s2", stamped: "Old", pinned: "Old", switching: "Work", running: true)));
		Assert.Equal("Old", running.ProfileName);
		Assert.Equal("Work", running.SwitchingToProfileName);
		Assert.True(running.HasSwitching);
		Assert.Contains("switching to Work", running.ProfileToolTip);

		// The turn ends and the queued switch is applied: the pin moves, the mark goes.
		_client.RaiseNotification(RemoteProtocol.Events.SessionChanged, new SessionChangedPayload(State("s2", stamped: "Old", pinned: "Work")));
		Assert.Equal("Work", running.ProfileName);
		Assert.False(running.HasSwitching);
	}

	[Fact]
	public async Task H1_ARunningSession_IsMarkedSwitchingToAtOnce_FromTheSwitchResult()
	{
		var tree = await TreeAsync(Session("s1", State("s1", stamped: "Old", pinned: "Old", running: true)));
		_client.ProfileSwitchResult = new ProfileSwitchResultDto("profile_1", "Work", NodeMoved: false, [], [new ProfileSwitchedSessionDto("s1", "s1", "id-Old", "Old")], [], []);
		Script("profile_1", "accept");
		var node = tree.FindNode("s1")!;

		await NewSwitch().SwitchAsync(node);

		Assert.Equal("Old", node.ProfileName);
		Assert.Equal("Work", node.SwitchingToProfileName);
	}

	[Fact]
	public async Task H1_TheMismatchWarning_UsesThePinOfTheWorkers()
	{
		var tree = await TreeAsync(Session("s1", State("s1", stamped: "Old", pinned: "Work"), workers: 1));
		_client.Workers["s1"] = [Session("w1", State("w1", stamped: "Work", pinned: "Old"), supervisor: "s1")];
		var session = tree.FindNode("s1")!;
		tree.ToggleExpandCommand.Execute(session);
		await tree.WhenIdleAsync();

		Assert.True(session.HasProfileMismatch);   // by the stamps alone both are "Work"; by the pins they differ
	}

	// ---- M1 -----------------------------------------------------------------------------------------------------------------------

	private async Task<TreeNodeViewModel> OrchestratorWithWorkersAsync(int workers)
	{
		var tree = await TreeAsync(Session("s1", State("s1", "Old", "Old"), workers: workers));
		_client.Workers["s1"] = Enumerable.Range(0, workers).Select(i => Session("w" + i, State("w" + i, "Old", "Old"), supervisor: "s1")).ToList();
		return tree.FindNode("s1")!;
	}

	[Fact]
	public async Task M1_ATreeOfSixHundredWorkers_IsCountedInFull()
	{
		var node = await OrchestratorWithWorkersAsync(600);
		Script("profile_1", Constants.Profiles.CascadeOnlyId);

		await NewSwitch().SwitchAsync(node);

		Assert.Contains("600 descendant", _presenter.Shown[1].Message);
		Assert.DoesNotContain("at least", _presenter.Shown[1].Message);
	}

	[Fact]
	public async Task M1_AReplyLargerThanTheBound_IsCountedInFull_WhenNothingIsLeftToRead()
	{
		var node = await OrchestratorWithWorkersAsync(10);
		Script("profile_1", Constants.Profiles.CascadeOnlyId);

		await NewSwitch(max: 4).SwitchAsync(node);

		Assert.Contains("10 descendant", _presenter.Shown[1].Message);   // one reply carries all ten and no worker has workers: nothing is left unread, so no "at least"
		Assert.DoesNotContain("at least", _presenter.Shown[1].Message);
	}

	[Fact]
	public async Task M1_TheBoundStopsFurtherReads_AndTheDialogCountsWhatWasRead()
	{
		// s1 has 2 workers, each with 3 workers: with a bound of 2 the grandchildren are never read.
		var tree = await TreeAsync(Session("s1", State("s1", "Old", "Old"), workers: 2));
		_client.Workers["s1"] = [Session("a", State("a", "Old", "Old"), workers: 3, supervisor: "s1"), Session("b", State("b", "Old", "Old"), workers: 3, supervisor: "s1")];
		_client.Workers["a"] = Enumerable.Range(0, 3).Select(i => Session("a" + i, State("a" + i, "Old", "Old"), supervisor: "a")).ToList();
		_client.Workers["b"] = Enumerable.Range(0, 3).Select(i => Session("b" + i, State("b" + i, "Old", "Old"), supervisor: "b")).ToList();
		Script("profile_1", Constants.Profiles.CascadeOnlyId);

		await NewSwitch(max: 2).SwitchAsync(tree.FindNode("s1"));

		Assert.Contains("at least 2 descendant", _presenter.Shown[1].Message);
		Assert.DoesNotContain(_client.Calls, c => c == "tree.workers:a");
	}

	// ---- M2 -----------------------------------------------------------------------------------------------------------------------

	[Fact]
	public async Task M2_AWorkersRunningFlag_DoesNotNotifyTheSupervisor_ButAProfileChangeDoes()
	{
		var tree = await TreeAsync(Session("s1", State("s1", "Work", "Work"), workers: 1));
		_client.Workers["s1"] = [Session("w1", State("w1", "Work", "Work"), supervisor: "s1")];
		var session = tree.FindNode("s1")!;
		tree.ToggleExpandCommand.Execute(session);
		await tree.WhenIdleAsync();
		var worker = tree.FindNode("w1")!;
		var changed = new List<string?>();
		session.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

		worker.State = worker.State! with { Running = true };
		worker.State = worker.State with { LastActivityUtc = DateTimeOffset.UtcNow };
		Assert.Empty(changed);

		worker.State = worker.State with { PinnedProfileName = "Other" };
		Assert.Contains(nameof(TreeNodeViewModel.HasProfileMismatch), changed);
		Assert.True(session.HasProfileMismatch);
	}

	// ---- L1 -----------------------------------------------------------------------------------------------------------------------

	[Fact]
	public async Task L1_SwitchingToTheDefaultProfile_PinsTheRowWithTheDaemonsDefaultLabel()
	{
		var tree = await TreeAsync(Session("s1", State("s1", "Work", "Work")));
		_client.ProfileSwitchResult = new ProfileSwitchResultDto("default", "[Sys] me@example.com", NodeMoved: true, [], [], [], []);
		Script("default");
		var node = tree.FindNode("s1")!;

		await NewSwitch().SwitchAsync(node);

		// The switch result names the Default profile by its system label; the daemon's state (and list_sessions) call it "default": the row agrees with the daemon.
		Assert.Equal("default", node.State!.PinnedProfileId);
		Assert.Equal("default", node.ProfileName);
	}

	[Fact]
	public async Task L1_ThePickListsTheDefaultProfileByItsSystemLabel()
	{
		var tree = await TreeAsync(Session("s1", State("s1", "Work", "Work")));
		Script("cancel");

		await NewSwitch().SwitchAsync(tree.FindNode("s1"));

		Assert.Equal("[Sys] me@example.com", _presenter.Shown[0].Choices.Single(c => c.Id == "default").Label);
	}

	// ---- L2 -----------------------------------------------------------------------------------------------------------------------

	[Fact]
	public async Task L2_WithoutTheProfilesAdminCapability_NothingIsOffered_AndNoProfileCallIsMade()
	{
		var client = new FakeTessynClient().AnnouncingProfileAdmin(announce: false);
		var flyout = NewFlyout();
		await flyout.AttachAsync(client);
		var switcher = new ProfileSwitchViewModel(Dialogs, _context, new ImmediateUiDispatcher(), NullLogger<ProfileSwitchViewModel>.Instance);
		switcher.Attach(client);
		var tree = await TreeAsync(Session("s1", State("s1", "Work", "Work")));
		Script("profile_1");

		await switcher.SwitchAsync(tree.FindNode("s1"));

		Assert.False(flyout.IsAvailable);
		Assert.False(flyout.IsSupported);
		Assert.False(switcher.IsAvailable);
		Assert.Empty(_presenter.Shown);
		Assert.DoesNotContain(client.Calls, c => c.StartsWith("profile.", StringComparison.Ordinal));
		Assert.DoesNotContain(_client.Calls, c => c.StartsWith("profile.switch", StringComparison.Ordinal));
	}

	[Fact]
	public async Task L2_TheSwitchCommand_IsDisabledWithoutTheCapability_AndEnabledWith()
	{
		var tree = await TreeAsync(Session("s1", State("s1", "Work", "Work")));
		var commands = new ShellCommands(new State.ClientSettingsStore(new NoState(), NullLogger<State.ClientSettingsStore>.Instance), NullLogger<ShellCommands>.Instance);
		var plain = NewHostWith(_client.AnnouncingProfileAdmin(announce: false));
		new ProfileShellModule(plain).Register(new ShellSlots(), commands);

		Assert.DoesNotContain(commands.BuildMenu(Constants.Menus.TreeNode, tree.FindNode("s1")), i => i.CommandId == Constants.Profiles.SwitchCommandId && i.IsEnabled);

		var withCapability = NewHostWith(_client.AnnouncingProfileAdmin());
		var commands2 = new ShellCommands(new State.ClientSettingsStore(new NoState(), NullLogger<State.ClientSettingsStore>.Instance), NullLogger<ShellCommands>.Instance);
		new ProfileShellModule(withCapability).Register(new ShellSlots(), commands2);
		Assert.Contains(commands2.BuildMenu(Constants.Menus.TreeNode, tree.FindNode("s1")), i => i.CommandId == Constants.Profiles.SwitchCommandId && i.IsEnabled);
	}

	private ProfileHost NewHostWith(FakeTessynClient client)
	{
		var connection = new ConnectionViewModel(new ScriptedLocator(), new FixedClientFactory(client), new ImmediateUiDispatcher(), NullLogger<ConnectionViewModel>.Instance, TimeSpan.FromMilliseconds(5));
		var switcher = new ProfileSwitchViewModel(Dialogs, _context, new ImmediateUiDispatcher(), NullLogger<ProfileSwitchViewModel>.Instance);
		switcher.Attach(client);
		return new ProfileHost(connection, NewFlyout(), switcher);
	}

	private sealed class NoState : State.IClientStateStore
	{
		public State.ClientStateModel State { get; } = new();

		public Task LoadAsync() => Task.CompletedTask;

		public void Update(Action<State.ClientStateModel> change) => change(State);

		public Task FlushAsync() => Task.CompletedTask;
	}

	// ---- L3 -----------------------------------------------------------------------------------------------------------------------

	[Fact]
	public async Task L3_AnOlderRefreshAnswerThatArrivesLast_DoesNotOverwriteTheNewerList()
	{
		var flyout = NewFlyout();
		await flyout.AttachAsync(_client);   // call 1, immediate
		var release = new TaskCompletionSource();
		_client.ProfileListDelay = call => call == 2 ? release.Task : Task.CompletedTask;

		var first = flyout.RefreshAsync();   // call 2: reads [default, Work] and waits
		_client.Profiles.Add(new("profile_2", "Newer", false, null, ProfileAuthState.LoggedIn, false));
		await flyout.RefreshAsync();         // call 3: reads the newer list at once
		Assert.Contains(flyout.Profiles, p => p.Id == "profile_2");

		release.SetResult();
		await first;

		Assert.Contains(flyout.Profiles, p => p.Id == "profile_2");   // the stale answer was dropped
	}

	[Fact]
	public async Task L3_AFailedRefreshesStatus_IsClearedByALaterSuccessfulOne_ButAnActionMessageSurvivesItsOwnRefresh()
	{
		var flyout = NewFlyout();
		await flyout.AttachAsync(_client);
		_client.ProfileException = new InvalidOperationException("offline");
		await flyout.RefreshAsync();
		Assert.Equal("offline", flyout.StatusText);

		_client.ProfileException = null;
		await flyout.RefreshAsync();
		Assert.Null(flyout.StatusText);

		await flyout.AuthenticateAsync(flyout.Profiles[1]);   // sets "Login ... started", then refreshes
		Assert.Contains("started", flyout.StatusText);
	}

	// ---- L4 -----------------------------------------------------------------------------------------------------------------------

	[Fact]
	public async Task L4_ACascadeSwitch_ReadsTheProfileListOnce()
	{
		var node = await OrchestratorWithWorkersAsync(2);
		Script("profile_1", Constants.Profiles.CascadeAllId);

		await NewSwitch().SwitchAsync(node);

		Assert.Equal(1, _client.Calls.Count(c => c == "profile.list"));
		Assert.Contains("\"Work\"", _presenter.Shown[1].Message);   // the target's name still reaches the dialog
	}

	// ---- L5 -----------------------------------------------------------------------------------------------------------------------

	[Fact]
	public async Task L5_WhenNothingChanged_AStatusLineSaysSo_AndNoDialogIsShownForIt()
	{
		var tree = await TreeAsync(Session("s1", State("s1", "Old", "Old")));
		_client.ProfileSwitchResult = new ProfileSwitchResultDto("profile_1", "Work", NodeMoved: false, [], [], [], []);
		Script("profile_1");
		var switcher = NewSwitch();

		await switcher.SwitchAsync(tree.FindNode("s1"));

		Assert.Contains("Nothing changed", switcher.StatusText);
		Assert.Single(_presenter.Shown);   // only the pick

		Script("profile_1");
		_client.ProfileSwitchResult = new ProfileSwitchResultDto("profile_1", "Work", NodeMoved: true, [], [], [], []);
		await switcher.SwitchAsync(tree.FindNode("s1"));
		Assert.Equal(string.Empty, switcher.StatusText);   // cleared by the next switch
	}

	[Fact]
	public async Task L5_ADaemonError_IsShownInTheStatusLine()
	{
		var tree = await TreeAsync(Session("s1", State("s1", "Old", "Old")));
		Script("profile_1");
		_client.ProfileException = new InvalidOperationException("boom");
		var switcher = NewSwitch();

		await switcher.SwitchAsync(tree.FindNode("s1"));

		Assert.Equal("boom", switcher.StatusText);
	}
}
