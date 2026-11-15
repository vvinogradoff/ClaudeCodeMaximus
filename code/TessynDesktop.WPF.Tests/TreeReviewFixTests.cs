using System.Windows.Input;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>The W2 review fixes (G1-G8): New session, no repeat on Delete, confirmed directory/group delete, a vanished open session, drop positions, failure refresh and wording.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TreeReviewFixTests : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-tests-" + Guid.NewGuid().ToString("N"));
	private readonly TreeBuilder _b = new();
	private readonly FakeTessynClient _client = new();
	private readonly FakeDialogPresenter _presenter = new();

	public void Dispose()
	{
		TestDirectory.Delete(_root);
	}

	private sealed class NoFolders : IFolderPicker
	{
		public Task<string?> PickAsync(string title) => Task.FromResult<string?>(null);
	}

	private TreeViewModel CreateTree()
	{
		_client.Directories =
		[
			_b.Directory("d1", "Repo", [_b.Group("g1", "Group A", _b.Session("s2", "In group"), _b.Session("s5", "Also in group"))], _b.Session("s1", "Top"), _b.Session("s4", "Fourth")),
			_b.Directory("d2", "Other", [], _b.Session("s3", "Third")),
		];
		var operations = new TreeOperations(_client, new DialogService(_presenter, new ImmediateUiDispatcher()), new NoFolders(), NullLogger<TreeOperations>.Instance);
		return new TreeViewModel(_client, new ImmediateUiDispatcher(), NullLogger<TreeViewModel>.Instance, TimeSpan.Zero, operations);
	}

	private async Task<TreeViewModel> LoadedAsync()
	{
		var tree = CreateTree();
		await tree.LoadAsync();
		tree.SelectNodeById("s2");
		return tree;
	}

	private void Answer(Action<DialogViewModel> answer) =>
		_presenter.Answer = d =>
		{
			answer(d);
			return Task.CompletedTask;
		};

	private void ServerChanged(Action change)
	{
		change();
		_client.TreeVersion++;
	}

	// ---------------------------------------------------------------- G1 New session

	[Fact]
	public async Task NewSession_OnAGroup_CreatesItThereAndSelectsIt()
	{
		var tree = await LoadedAsync();
		var selected = new List<string>();
		tree.SelectionChanged += (_, node) => selected.Add(node.Id);
		_client.CreateSessionHandler = (anchor, name) =>
		{
			ServerChanged(() => _client.Directories =
			[
				_b.Directory("d1", "Repo", [_b.Group("g1", "Group A", _b.Session("s2", "In group"), _b.Session("s5", "Also in group"), _b.Session("n1", name))], _b.Session("s1", "Top"), _b.Session("s4", "Fourth")),
				_client.Directories[1],
			]);
			return new CreateSessionResult(_b.Session("n1", name), "d1", "g1");
		};
		Answer(d =>
		{
			Assert.Equal("New session", d.InputText);   // a default name, ready to be accepted
			d.InputText = "  Plan the release ";
			d.AcceptCommand.Execute(null);
		});

		await tree.Operations!.AddSessionAsync(tree.FindNode("g1"));

		Assert.Contains("tree.createSession:g1:Plan the release", _client.Calls);
		Assert.Equal("n1", tree.SelectedNode?.Id);
		Assert.Equal("n1", selected.Last());   // the selection is what opens it in the pane
	}

	[Fact]
	public async Task NewSession_OnASessionGoesBesideIt_AndIsNotOfferedForAWorkerOrWhenCancelled()
	{
		var tree = await LoadedAsync();
		_client.CreateSessionHandler = (anchor, name) => new CreateSessionResult(_b.Session("n1", name), "d1", null);
		Answer(d => d.AcceptCommand.Execute(null));

		await tree.Operations!.AddSessionAsync(tree.FindNode("s4"));
		Assert.Contains("tree.createSession:d1:New session", _client.Calls);

		_client.Calls.Clear();
		await tree.Operations.AddSessionAsync(new TreeNodeViewModel("w1", TreeNodeKind.Worker, "W", tree.FindNode("s1")));
		Answer(d => d.CancelCommand.Execute(null));
		await tree.Operations.AddSessionAsync(tree.FindNode("d1"));
		Assert.DoesNotContain(_client.Calls, c => c.StartsWith("tree.createSession", StringComparison.Ordinal));
	}

	[Fact]
	public async Task NewSession_IsInTheMenuAndAToolbarCommand()
	{
		var tree = await LoadedAsync();
		var commands = new ShellCommands(new ClientSettingsStore(new MemoryState(), NullLogger<ClientSettingsStore>.Instance), NullLogger<ShellCommands>.Instance);
		var context = new TreeContext { Current = tree };
		new TreeShellModule(context).Register(new ShellSlots(), commands);

		var entry = commands.BuildMenu(Constants.Menus.TreeNode, tree.FindNode("d1")).First();
		Assert.Equal(Constants.TreeCommands.AddSession, entry.CommandId);
		Assert.True(entry.IsEnabled);
		tree.SelectedNode = tree.FindNode("g1");
		Assert.True(tree.AddSessionCommand.CanExecute(null));
		tree.SelectedNode = tree.FindNode("s4");
		Assert.True(tree.AddSessionCommand.CanExecute(null));
	}

	private sealed class MemoryState : IClientStateStore
	{
		public ClientStateModel State { get; } = new();

		public Task LoadAsync() => Task.CompletedTask;

		public void Update(Action<ClientStateModel> change) => change(State);

		public Task FlushAsync() => Task.CompletedTask;
	}

	// ---------------------------------------------------------------- G2 repeat, one operation at a time

	[Fact]
	public void AHeldKey_RunsACommandThatIgnoresRepeatOnce_AndSwallowsTheRepeats()
	{
		var commands = new ShellCommands(new ClientSettingsStore(new MemoryState(), NullLogger<ClientSettingsStore>.Instance), NullLogger<ShellCommands>.Instance);
		var ran = 0;
		commands.Register(new ShellCommand { Id = "once", Title = "Once", DefaultGesture = "F7", IgnoreRepeat = true, ExecuteAsync = _ => { ran++; return Task.CompletedTask; } });
		commands.Register(new ShellCommand { Id = "again", Title = "Again", DefaultGesture = "F8", ExecuteAsync = _ => { ran += 10; return Task.CompletedTask; } });

		Assert.True(commands.TryExecuteGesture(Key.F7, ModifierKeys.None, false, isRepeat: false));
		Assert.True(commands.TryExecuteGesture(Key.F7, ModifierKeys.None, false, isRepeat: true));   // taken, not run
		Assert.True(commands.TryExecuteGesture(Key.F7, ModifierKeys.None, false, isRepeat: true));
		Assert.Equal(1, ran);

		Assert.True(commands.TryExecuteGesture(Key.F8, ModifierKeys.None, false, isRepeat: true));   // an ordinary command repeats
		Assert.Equal(11, ran);
	}

	[Fact]
	public async Task TheTreeDestructiveKeysIgnoreRepeat()
	{
		var tree = await LoadedAsync();
		var commands = new ShellCommands(new ClientSettingsStore(new MemoryState(), NullLogger<ClientSettingsStore>.Instance), NullLogger<ShellCommands>.Instance);
		new TreeShellModule(new TreeContext { Current = tree }).Register(new ShellSlots(), commands);

		Assert.True(commands.Find(Constants.TreeCommands.Delete)!.IgnoreRepeat);
		Assert.True(commands.Find(Constants.TreeCommands.Rename)!.IgnoreRepeat);
	}

	[Fact]
	public async Task ASecondDeleteWhileOneIsInFlight_IsDropped()
	{
		var tree = await LoadedAsync();
		var gate = new TaskCompletionSource();
		_client.DeleteNodeHandler = (_, _) => new TreeDeleteResult(true, null);
		Answer(d => d.AcceptCommand.Execute(null));
		_presenter.Answer = async d =>
		{
			await gate.Task;
			d.AcceptCommand.Execute(null);
		};

		var first = tree.Operations!.DeleteAsync(tree.FindNode("g1"));   // waits in its confirmation
		var second = tree.Operations.DeleteAsync(tree.FindNode("d2"));
		await second;
		gate.SetResult();
		await first;

		Assert.Equal(["tree.deleteNode:g1:False"], _client.Calls.Where(c => c.StartsWith("tree.deleteNode", StringComparison.Ordinal)));
		Assert.Single(_presenter.Shown);
	}

	// ---------------------------------------------------------------- G4 confirmation by kind

	[Fact]
	public async Task DeletingADirectory_IsAlwaysConfirmed_WithItsScopeInTheText()
	{
		var tree = await LoadedAsync();
		_client.DeleteNodeHandler = (_, _) => new TreeDeleteResult(true, null);
		string? text = null;
		Answer(d =>
		{
			text = d.Message;
			d.CancelCommand.Execute(null);
		});

		await tree.Operations!.DeleteAsync(tree.FindNode("d2"));

		Assert.DoesNotContain(_client.Calls, c => c.StartsWith("tree.deleteNode", StringComparison.Ordinal));
		Assert.Contains("\"Other\"", text);
		Assert.Contains("entry", text);
		Assert.Contains("local tool profile", text);
		Assert.Contains("not touched", text);

		Answer(d => d.AcceptCommand.Execute(null));
		await tree.Operations.DeleteAsync(tree.FindNode("d2"));
		Assert.Contains("tree.deleteNode:d2:False", _client.Calls);
	}

	[Fact]
	public async Task DeletingAnEmptyGroup_IsConfirmed_ButAnEmptySessionIsNot()
	{
		var tree = await LoadedAsync();
		_client.DeleteNodeHandler = (_, _) => new TreeDeleteResult(true, null);
		Answer(d => d.CancelCommand.Execute(null));

		await tree.Operations!.DeleteAsync(tree.FindNode("g1"));
		Assert.Single(_presenter.Shown);
		Assert.DoesNotContain(_client.Calls, c => c.StartsWith("tree.deleteNode", StringComparison.Ordinal));

		_presenter.Shown.Clear();
		await tree.Operations.DeleteAsync(tree.FindNode("s4"));
		Assert.Empty(_presenter.Shown);
		Assert.Contains("tree.deleteNode:s4:False", _client.Calls);
	}

	// ---------------------------------------------------------------- G3 the open session disappears

	[Fact]
	public async Task WhenTheSelectedSessionIsDeletedElsewhere_SelectionIsLost_AndTheSelectionIsCleared()
	{
		var tree = await LoadedAsync();   // s2 selected
		var lost = new List<string>();
		tree.SelectionLost += (_, id) => lost.Add(id);
		ServerChanged(() => _client.Directories = [_b.Directory("d1", "Repo", [_b.Group("g1", "Group A", _b.Session("s5", "Also in group"))], _b.Session("s1", "Top")), _client.Directories[1]]);

		_client.RaiseNotification("tree.changed", new TreeChangedPayload(_client.TreeVersion));
		await tree.WhenIdleAsync();

		Assert.Equal(["s2"], lost);
		Assert.Null(tree.SelectedNode);
		Assert.Null(tree.SelectedNodeId);
	}

	[Fact]
	public async Task ACollapsedOrUnrelatedChange_DoesNotLoseTheSelection()
	{
		var tree = await LoadedAsync();
		var lost = new List<string>();
		tree.SelectionLost += (_, id) => lost.Add(id);
		await tree.ToggleExpandCommand.ExecuteAsync(tree.FindNode("g1"));   // collapse: s2 is hidden but exists
		ServerChanged(() => { });

		await tree.RefreshAsync();

		Assert.Empty(lost);
		Assert.Equal("s2", tree.SelectedNodeId);
	}

	[Fact]
	public async Task TheSessionPane_IsClearedWhenItsSessionIsDeleted()
	{
		_client.Directories = [_b.Directory("d1", "Repo", [], _b.Session("s1", "Top"), _b.Session("s4", "Fourth"))];
		foreach (var id in new[] { "s1", "s4" })
			_client.Sessions[id] = FakeSession.WithEntries(5, "Session " + id);

		var store = new JsonClientStateStore(new AppPaths(_root), NullLogger<JsonClientStateStore>.Instance, TimeSpan.FromMilliseconds(10));
		await store.LoadAsync();
		var ui = new ImmediateUiDispatcher();
		var locator = new ScriptedLocator();
		locator.Results.Enqueue(new DaemonDiscoveryResult(DaemonDiscoveryStatus.Found, new DaemonEndpoint(new Uri("http://127.0.0.1:1/tdcp"), "t", "r", 1)));
		var connection = new ConnectionViewModel(locator, new FixedClientFactory(_client), ui, NullLogger<ConnectionViewModel>.Instance, TimeSpan.FromMilliseconds(5));
		var main = new MainViewModel(
			connection,
			c => new TreeViewModel(c, ui, NullLogger<TreeViewModel>.Instance, TimeSpan.Zero),
			c => new SessionPaneViewModel(c, new FakeTranscriptSurface(), new MarkdigEntryRenderer(new UrlPolicy()), new FakeUrlLauncher(), ui,
				new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance), NullLogger<SessionPaneViewModel>.Instance),
			store, NullLogger<MainViewModel>.Instance);
		await main.StartAsync(CancellationToken.None);
		await main.Tree!.WhenIdleAsync();
		main.Tree.SelectNodeById("s4");
		await main.Session!.OpenAsync("s4", "Fourth");
		Assert.True(main.Session.HasSession);

		ServerChanged(() => _client.Directories = [_b.Directory("d1", "Repo", [], _b.Session("s1", "Top"))]);
		_client.RaiseNotification("tree.changed", new TreeChangedPayload(_client.TreeVersion));
		await main.Tree.WhenIdleAsync();

		Assert.False(main.Session.HasSession);
		Assert.Null(main.Session.NodeId);
		Assert.Null(store.State.LastSelectedNodeId);
	}

	// ---------------------------------------------------------------- G5 drop positions

	[Fact]
	public async Task DroppingOnAContainer_AppendsAtTheEnd()
	{
		var tree = await LoadedAsync();
		_client.MoveTargetsHandler = _ => new TreeMoveTargetsResult(["d1", "g1"]);
		var ops = tree.Operations!;
		var source = tree.FindNode("s4")!;
		await ops.BeginDrag(source);

		await ops.DropAsync(source, tree.FindNode("g1"));   // g1 holds s2 and s5

		Assert.Contains("tree.moveNode:s4:g1:2", _client.Calls);
	}

	[Fact]
	public async Task DroppingOnASession_PlacesTheMovedOneAtThatSessionsIndex()
	{
		var tree = await LoadedAsync();
		_client.MoveTargetsHandler = _ => new TreeMoveTargetsResult(["d1", "g1"]);
		var ops = tree.Operations!;
		var source = tree.FindNode("s4")!;
		await ops.BeginDrag(source);

		await ops.DropAsync(source, tree.FindNode("s5"));   // second session of g1

		Assert.Contains("tree.moveNode:s4:g1:1", _client.Calls);
	}

	[Fact]
	public async Task DroppingASessionOnItself_DoesNothing()
	{
		var tree = await LoadedAsync();
		_client.MoveTargetsHandler = _ => new TreeMoveTargetsResult(["d1"]);
		var source = tree.FindNode("s4")!;
		await tree.Operations!.BeginDrag(source);

		await tree.Operations.DropAsync(source, source);

		Assert.DoesNotContain(_client.Calls, c => c.StartsWith("tree.moveNode", StringComparison.Ordinal));
	}

	[Fact]
	public async Task MoveTo_AppendsAtTheEndOfTheChosenPlace()
	{
		var tree = await LoadedAsync();
		_client.MoveTargetsHandler = _ => new TreeMoveTargetsResult(["d1", "g1"]);
		Answer(d => d.ChooseCommand.Execute(d.Choices[0]));

		await tree.Operations!.MoveToAsync(tree.FindNode("s4"));

		Assert.Contains("tree.moveNode:s4:g1:2", _client.Calls);
	}

	// ---------------------------------------------------------------- G6, G7 failures

	[Fact]
	public async Task AFailedOperation_RefreshesTheTree_AndExplainsAMissingNodeInWords()
	{
		var tree = await LoadedAsync();
		_client.RenameNodeHandler = (_, _) => throw new TessynRpcException(-32000, "Node not found");
		ServerChanged(() => _client.Directories = [_client.Directories[0], _b.Directory("d2", "Other", [], _b.Session("s3", "Third"), _b.Session("s9", "Appeared"))]);
		Answer(d =>
		{
			if (d.IsInput)
			{
				d.InputText = "x";
				d.AcceptCommand.Execute(null);
			}
			else
			{
				d.CancelCommand.Execute(null);
			}
		});

		await tree.Operations!.RenameAsync(tree.FindNode("s1"));

		var message = _presenter.Shown.Last();
		Assert.Contains("removed meanwhile", message.Message);
		Assert.DoesNotContain("Node not found", message.Message);
		Assert.NotNull(tree.FindNode("s9"));   // the tree was refreshed after the failure
	}

	[Fact]
	public async Task AMoveThatDidNotHappen_SaysSoInsteadOfRepeatingItsTitle()
	{
		var tree = await LoadedAsync();
		_client.MoveTargetsHandler = _ => new TreeMoveTargetsResult(["d1", "g1"]);
		_client.MoveNodeHandler = (_, _, _) => new TreeMoveResult(false);
		Answer(d =>
		{
			if (d.IsChoice)
				d.ChooseCommand.Execute(d.Choices[0]);
			else
				d.CancelCommand.Execute(null);
		});

		await tree.Operations!.MoveToAsync(tree.FindNode("s4"));

		var message = _presenter.Shown.Last();
		Assert.NotEqual(message.Title, message.Message);
		Assert.Equal(Constants.TreeTexts.MoveNotDone, message.Message);
	}
}
