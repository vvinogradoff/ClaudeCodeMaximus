using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>ISSUE.013 R1 in the WPF client (W5): the profile pick, the cascade dialog over a session's workers, and the mapping of the daemon's moved, queued and failed lists.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ProfileSwitchViewModelTests
{
	private readonly FakeTessynClient _client = new FakeTessynClient().AnnouncingProfileAdmin();
	private readonly FakeDialogPresenter _presenter = new();
	private readonly TreeContext _context = new();
	private readonly ProfileSwitchViewModel _switch;
	private TreeViewModel _tree = null!;

	public ProfileSwitchViewModelTests()
	{
		_client.Profiles =
		[
			new("default", "[Sys] me@example.com", true, "me@example.com", ProfileAuthState.Unknown, true),
			new("profile_1", "Work", false, null, ProfileAuthState.LoggedIn, false),
			new("profile_2", "Spare", false, null, ProfileAuthState.LoggedOut, false),
		];
		_switch = new ProfileSwitchViewModel(new DialogService(_presenter, new ImmediateUiDispatcher()), _context, new ImmediateUiDispatcher(), NullLogger<ProfileSwitchViewModel>.Instance);
		_switch.Attach(_client);
	}

	private static TreeSessionDto Session(string id, string name, string profile, bool running = false, int workers = 0, string? supervisor = null) =>
		new(id, name, workers, supervisor, new SessionStateDto(id, running, false, null, false, null, null, false, profile));

	/// <summary>s1 (on "[Sys] me@example.com") with workers w1 (idle, Work), w2 (running, Work) and w3 under w1.</summary>
	private async Task<TreeNodeViewModel> OrchestratorAsync()
	{
		_client.Directories = [new TreeDirectoryDto("d1", "Repo", false, [], [Session("s1", "Orchestrator", "[Sys] me@example.com", workers: 2), Session("solo", "Solo", "[Sys] me@example.com")])];
		_client.Workers["s1"] = [Session("w1", "Worker idle", "Work", workers: 1, supervisor: "s1"), Session("w2", "Worker running", "Work", running: true, supervisor: "s1")];
		_client.Workers["w1"] = [Session("w3", "Nested", "Work", supervisor: "w1")];
		_tree = new TreeViewModel(_client, new ImmediateUiDispatcher(), NullLogger<TreeViewModel>.Instance, TimeSpan.Zero);
		_context.Current = _tree;
		await _tree.LoadAsync();
		return _tree.FindNode("s1")!;
	}

	/// <summary>Answers the dialogs in order: a choice id, or "cancel".</summary>
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

	[Fact]
	public async Task ASessionWithWorkers_AsksToSwitchThemToo_ListsThemAndSendsIncludeDescendants()
	{
		var node = await OrchestratorAsync();
		Script("profile_1", Constants.Profiles.CascadeAllId);

		await _switch.SwitchAsync(node);

		Assert.Equal(2, _presenter.Shown.Count);
		var cascade = _presenter.Shown[1];
		Assert.Contains("3 descendant", cascade.Message);
		Assert.Contains("Worker idle (idle, on Work)", cascade.Message);
		Assert.Contains("Nested (idle, on Work)", cascade.Message);
		Assert.Contains("Worker running (running, on Work): queued", cascade.Message);
		Assert.Contains("Work", cascade.Message);
		Assert.Equal([Constants.Profiles.CascadeAllId, Constants.Profiles.CascadeOnlyId], cascade.Choices.Select(c => c.Id));
		Assert.Contains("profile.switch:s1:profile_1:True", _client.Calls);
	}

	[Fact]
	public async Task SwitchOnlyThisSession_SendsNoDescendants()
	{
		var node = await OrchestratorAsync();
		Script("profile_1", Constants.Profiles.CascadeOnlyId);

		await _switch.SwitchAsync(node);

		Assert.Contains("profile.switch:s1:profile_1:False", _client.Calls);
	}

	[Fact]
	public async Task CancellingTheCascade_SwitchesNothing()
	{
		var node = await OrchestratorAsync();
		Script("profile_1", "cancel");

		await _switch.SwitchAsync(node);

		Assert.DoesNotContain(_client.Calls, c => c.StartsWith("profile.switch", StringComparison.Ordinal));
		Assert.Null(_switch.LastSummary);
		Assert.Null(_switch.LastError);
	}

	[Fact]
	public async Task CancellingThePick_AsksNoCascadeAndSwitchesNothing()
	{
		var node = await OrchestratorAsync();
		Script("cancel");

		await _switch.SwitchAsync(node);

		Assert.Single(_presenter.Shown);
		Assert.DoesNotContain(_client.Calls, c => c.StartsWith("profile.switch", StringComparison.Ordinal));
	}

	[Fact]
	public async Task ASessionWithoutWorkers_AsksOnlyForTheProfile()
	{
		await OrchestratorAsync();
		Script("profile_1");

		await _switch.SwitchAsync(_tree.FindNode("solo"));

		Assert.Single(_presenter.Shown);
		Assert.Contains("profile.switch:solo:profile_1:False", _client.Calls);
	}

	[Fact]
	public async Task ThePickLists_EveryProfileExceptTheOneTheSessionIsOn_AndMarksALoggedOutOne()
	{
		var node = await OrchestratorAsync();
		Script("cancel");

		await _switch.SwitchAsync(node);

		var pick = _presenter.Shown[0];
		Assert.Equal(["profile_1", "profile_2"], pick.Choices.Select(c => c.Id));   // the Default profile is where the session is
		Assert.Contains("logged out", pick.Choices[1].Label);
	}

	[Fact]
	public async Task TheResult_ListsQueuedAndFailedSessions_AndMovesTheBadgesOfTheMovedOnes()
	{
		var node = await OrchestratorAsync();
		await _tree.LoadAsync();
		_client.ProfileSwitchResult = new ProfileSwitchResultDto(
			"profile_1", "Work", NodeMoved: true,
			Moved: [new("w1", "Worker idle", "default", "[Sys] me@example.com")],
			Queued: [new("w2", "Worker running", "default", "[Sys] me@example.com")],
			OldConfigPaths: ["C:\\old\\a", "C:\\old\\b"],
			Failed: [new("w3", "Nested", ["C:\\held\\file.jsonl"])]);
		Script("profile_1", Constants.Profiles.CascadeAllId, "accept");

		await _switch.SwitchAsync(node);

		var summary = _switch.LastSummary!;
		Assert.Equal(2, summary.MovedCount);   // the session and one worker
		Assert.False(summary.NodeQueued);
		Assert.Equal(["Worker running"], summary.QueuedNames);
		Assert.Equal(["Nested"], summary.FailedNames);
		Assert.Equal(2, summary.OldPathCount);
		Assert.Equal("Work", node.ProfileName);

		var report = _presenter.Shown[2];
		Assert.Equal(Constants.Profiles.ResultTitle, report.Title);
		Assert.Contains("Moved 2 session(s) to \"Work\"", report.Message);
		Assert.Contains("Queued", report.Message);
		Assert.Contains("Worker running", report.Message);
		Assert.Contains("Nested", report.Message);
		Assert.Contains("2 path(s)", report.Message);
	}

	[Fact]
	public async Task ARunningSession_IsReportedAsQueued_AndKeepsItsBadge()
	{
		var node = await OrchestratorAsync();
		_client.ProfileSwitchResult = new ProfileSwitchResultDto(
			"profile_1", "Work", NodeMoved: false, Moved: [], Queued: [new("s1", "Orchestrator", "default", "[Sys] me@example.com")], OldConfigPaths: [], Failed: []);
		Script("profile_1", Constants.Profiles.CascadeOnlyId, "accept");

		await _switch.SwitchAsync(node);

		Assert.True(_switch.LastSummary!.NodeQueued);
		Assert.Equal(0, _switch.LastSummary.MovedCount);
		Assert.Equal("[Sys] me@example.com", node.ProfileName);
		Assert.Contains("is running", _presenter.Shown[2].Message);
	}

	[Fact]
	public async Task AFullSuccess_ShowsNoReportDialog()
	{
		var node = await OrchestratorAsync();
		_client.ProfileSwitchResult = new ProfileSwitchResultDto(
			"profile_1", "Work", NodeMoved: true, Moved: [new("w1", "Worker idle", "default", "x")], Queued: [], OldConfigPaths: [], Failed: []);
		Script("profile_1", Constants.Profiles.CascadeAllId);

		await _switch.SwitchAsync(node);

		Assert.Equal(2, _presenter.Shown.Count);
		Assert.False(_switch.LastSummary!.NeedsAttention);
	}

	[Fact]
	public async Task ADaemonError_IsKeptAsTheLastError_NotThrown()
	{
		var node = await OrchestratorAsync();
		Script("profile_1", Constants.Profiles.CascadeOnlyId);
		_client.ProfileException = new InvalidOperationException("boom");

		await _switch.SwitchAsync(node);

		Assert.Equal("boom", _switch.LastError);
	}

	[Fact]
	public async Task OnlySessionsCanBeSwitched()
	{
		await OrchestratorAsync();

		Assert.False(ProfileSwitchViewModel.CanSwitch(_tree.FindNode("d1")));
		Assert.False(ProfileSwitchViewModel.CanSwitch(null));
		Assert.True(ProfileSwitchViewModel.CanSwitch(_tree.FindNode("s1")));
	}

	[Fact]
	public async Task TheCommand_IsInTheTreeMenu_AndSwitchesTheNodeItWasOpenedOn()
	{
		var node = await OrchestratorAsync();
		var commands = new ShellCommands(new State.ClientSettingsStore(new NoState(), NullLogger<State.ClientSettingsStore>.Instance), NullLogger<ShellCommands>.Instance);
		var host = NewHost();
		new ProfileShellModule(host).Register(new ShellSlots(), commands);
		Script("profile_1", Constants.Profiles.CascadeOnlyId);

		var items = commands.BuildMenu(Constants.Menus.TreeNode, node);
		Assert.Contains(items, i => i.CommandId == Constants.Profiles.SwitchCommandId && i.IsEnabled);
		Assert.DoesNotContain(commands.BuildMenu(Constants.Menus.TreeNode, _tree.FindNode("d1")), i => i.CommandId == Constants.Profiles.SwitchCommandId && i.IsEnabled);

		await commands.ExecuteAsync(Constants.Profiles.SwitchCommandId, node);

		Assert.Contains("profile.switch:s1:profile_1:False", _client.Calls);
	}

	private ProfileHost NewHost()
	{
		var dialogs = new DialogService(_presenter, new ImmediateUiDispatcher());
		var flyout = new ProfileViewModel(dialogs, new ImmediateUiDispatcher(), NullLogger<ProfileViewModel>.Instance);
		var connection = new ConnectionViewModel(new ScriptedLocator(), new FixedClientFactory(_client), new ImmediateUiDispatcher(), NullLogger<ConnectionViewModel>.Instance, TimeSpan.FromMilliseconds(5));
		return new ProfileHost(connection, flyout, _switch);
	}

	private sealed class NoState : State.IClientStateStore
	{
		public State.ClientStateModel State { get; } = new();

		public Task LoadAsync() => Task.CompletedTask;

		public void Update(Action<State.ClientStateModel> change) => change(State);

		public Task FlushAsync() => Task.CompletedTask;
	}
}
