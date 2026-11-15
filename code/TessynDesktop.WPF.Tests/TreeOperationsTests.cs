using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>The tree operations (W2): rename, delete with the reason it was blocked, add directory and group, move by menu and by drag, git origins, tooltips.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TreeOperationsTests
{
	private sealed class FakeFolderPicker : IFolderPicker
	{
		public string? Answer { get; set; }

		public int Asked { get; private set; }

		public Task<string?> PickAsync(string title)
		{
			Asked++;
			return Task.FromResult(Answer);
		}
	}

	private readonly TreeBuilder _b = new();
	private readonly FakeTessynClient _client = new();
	private readonly FakeDialogPresenter _presenter = new();
	private readonly FakeFolderPicker _folders = new();

	private static TessynDesktop.Models.Remote.TreeNodeInfoItem Info(string id, string kind, string path, string workingDirectory, string? origin, string label) => new(id, kind, path, workingDirectory, origin, label);

	/// <summary>d1 (Repo): group g1 (Group A) with s2; sessions s1 (with 2 workers), s4. d2 (Other): s3.</summary>
	private TreeViewModel CreateTree()
	{
		_client.Directories =
		[
			_b.Directory("d1", "Repo", [_b.Group("g1", "Group A", _b.Session("s2", "In group"))], _b.Session("s1", "Top session", workers: 2), _b.Session("s4", "Fourth")),
			_b.Directory("d2", "Other", [], _b.Session("s3", "Third")),
		];
		var dialogs = new DialogService(_presenter, new ImmediateUiDispatcher());
		var operations = new TreeOperations(_client, dialogs, _folders, NullLogger<TreeOperations>.Instance);
		return new TreeViewModel(_client, new ImmediateUiDispatcher(), NullLogger<TreeViewModel>.Instance, TimeSpan.Zero, operations,
			new TreeNodeInfoLoader(_client, NullLogger<TreeNodeInfoLoader>.Instance));
	}

	private async Task<TreeViewModel> LoadedTreeAsync()
	{
		var tree = CreateTree();
		await tree.LoadAsync();
		tree.SelectNodeById("s2");   // expands d1 and g1
		return tree;
	}

	private void ServerChanged(Action change)
	{
		change();
		_client.TreeVersion++;
	}

	private void Answer(Action<DialogViewModel> answer) =>
		_presenter.Answer = d =>
		{
			answer(d);
			return Task.CompletedTask;
		};

	private static void Accept(DialogViewModel d) => d.AcceptCommand.Execute(null);

	// ---------------------------------------------------------------- rename

	[Fact]
	public async Task Rename_AsksForTheNewNameCallsTheDaemonAndShowsTheNewLabel()
	{
		var tree = await LoadedTreeAsync();
		var node = tree.FindNode("s1")!;
		_client.RenameNodeHandler = (id, name) =>
		{
			ServerChanged(() => _client.Directories = [_b.Directory("d1", "Repo", [_b.Group("g1", "Group A", _b.Session("s2", "In group"))], _b.Session("s1", name, workers: 2), _b.Session("s4", "Fourth")), _client.Directories[1]]);
			return new TreeRenameResult(name);
		};
		Answer(d =>
		{
			Assert.Equal("Rename", d.Title);
			Assert.Equal("Top session", d.InputText);
			d.InputText = "  Renamed  ";
			Accept(d);
		});

		tree.SelectedNode = node;   // the toolbar command acts on the selected node
		await tree.RenameCommand.ExecuteAsync(null);

		Assert.Contains("tree.renameNode:s1:Renamed", _client.Calls);
		Assert.Equal("Renamed", tree.FindNode("s1")!.Label);
	}

	[Fact]
	public async Task Rename_ToTheSameNameOrCancelled_CallsNothing()
	{
		var tree = await LoadedTreeAsync();
		var node = tree.FindNode("s1")!;

		Answer(d =>
		{
			d.InputText = "Top session";
			Accept(d);
		});
		await tree.Operations!.RenameAsync(node);
		Answer(d => d.CancelCommand.Execute(null));
		await tree.Operations!.RenameAsync(node);

		Assert.DoesNotContain(_client.Calls, c => c.StartsWith("tree.renameNode", StringComparison.Ordinal));
	}

	[Fact]
	public async Task Rename_RefusesABlankNameInTheDialogItself()
	{
		var tree = await LoadedTreeAsync();
		string? shownError = null;
		Answer(d =>
		{
			d.InputText = "   ";
			Accept(d);
			shownError = d.Error;
			Assert.False(d.Outcome.IsCompleted);   // the dialog stays open
			d.CancelCommand.Execute(null);
		});

		await tree.Operations!.RenameAsync(tree.FindNode("s1"));

		Assert.Equal(Constants.TreeTexts.NameRequired, shownError);
		Assert.DoesNotContain(_client.Calls, c => c.StartsWith("tree.renameNode", StringComparison.Ordinal));
	}

	[Fact]
	public async Task Rename_RefusedByTheDaemon_ShowsItsMessage()
	{
		var tree = await LoadedTreeAsync();
		_client.RenameNodeHandler = (_, _) => throw new TessynRpcException(-32000, "A directory cannot be renamed");
		var inputAnswered = false;
		Answer(d =>
		{
			if (d.IsInput)
			{
				d.InputText = "x";
				inputAnswered = true;
				Accept(d);
			}
			else
			{
				d.CancelCommand.Execute(null);
			}
		});

		await tree.Operations!.RenameAsync(tree.FindNode("s1"));

		Assert.True(inputAnswered);
		var message = _presenter.Shown.Last();
		Assert.Equal(DialogKind.Confirm, message.Kind);
		Assert.Contains("A directory cannot be renamed", message.Message);
	}

	[Fact]
	public async Task Rename_IsNotOfferedForADirectory()
	{
		var tree = await LoadedTreeAsync();

		Assert.False(TreeOperations.CanRename(tree.FindNode("d1")));
		Assert.True(TreeOperations.CanRename(tree.FindNode("g1")));
		Assert.True(TreeOperations.CanRename(tree.FindNode("s1")));
		await tree.Operations!.RenameAsync(tree.FindNode("d1"));
		Assert.Empty(_presenter.Shown);
	}

	// ---------------------------------------------------------------- delete

	[Fact]
	public async Task Delete_OfSomethingEmpty_NeedsNoQuestion()
	{
		var tree = await LoadedTreeAsync();
		_client.DeleteNodeHandler = (id, force) =>
		{
			ServerChanged(() => _client.Directories = [_client.Directories[0], _client.Directories[1]]);
			return new TreeDeleteResult(true, null);
		};

		await tree.Operations!.DeleteAsync(tree.FindNode("s4"));

		Assert.Equal(["tree.deleteNode:s4:False"], _client.Calls.Where(c => c.StartsWith("tree.deleteNode", StringComparison.Ordinal)));
		Assert.Empty(_presenter.Shown);
	}

	[Theory]
	[InlineData("sessionFile", "transcript file")]
	[InlineData("workers", "worker sessions")]
	public async Task Delete_BlockedByContent_ExplainsWhyAndRepeatsWithForceWhenConfirmed(string blockedBy, string reasonText)
	{
		var tree = await LoadedTreeAsync();
		_client.DeleteNodeHandler = (_, force) => force ? new TreeDeleteResult(true, null) : new TreeDeleteResult(false, blockedBy);
		Answer(d =>
		{
			Assert.True(d.IsConfirm);
			Assert.True(d.IsDestructive);
			Assert.Contains("Top session", d.Message);
			Assert.Contains(reasonText, d.Message);
			Accept(d);
		});

		await tree.Operations!.DeleteAsync(tree.FindNode("s1"));

		Assert.Equal(["tree.deleteNode:s1:False", "tree.deleteNode:s1:True"], _client.Calls.Where(c => c.StartsWith("tree.deleteNode", StringComparison.Ordinal)));
	}

	[Fact]
	public async Task Delete_BlockedByContent_KeepsTheNodeWhenTheUserSaysNo()
	{
		var tree = await LoadedTreeAsync();
		_client.DeleteNodeHandler = (_, force) => new TreeDeleteResult(force, force ? null : "sessionFile");
		Answer(d => d.CancelCommand.Execute(null));

		await tree.Operations!.DeleteAsync(tree.FindNode("s1"));

		Assert.Equal(["tree.deleteNode:s1:False"], _client.Calls.Where(c => c.StartsWith("tree.deleteNode", StringComparison.Ordinal)));
		Assert.NotNull(tree.FindNode("s1"));
	}

	[Theory]
	[InlineData("running", "is running")]
	[InlineData("notEmpty", "is not empty")]
	public async Task Delete_BlockedForGood_OnlyExplainsAndNeverForces(string blockedBy, string reasonText)
	{
		var tree = await LoadedTreeAsync();
		_client.DeleteNodeHandler = (_, _) => new TreeDeleteResult(false, blockedBy);
		Answer(d => d.CancelCommand.Execute(null));

		await tree.Operations!.DeleteAsync(tree.FindNode("g1"));

		Assert.Equal(["tree.deleteNode:g1:False"], _client.Calls.Where(c => c.StartsWith("tree.deleteNode", StringComparison.Ordinal)));
		Assert.Contains(reasonText, Assert.Single(_presenter.Shown).Message);
	}

	// ---------------------------------------------------------------- add

	[Fact]
	public async Task NewGroup_OnASession_GoesIntoItsParentAndIsRevealed()
	{
		var tree = await LoadedTreeAsync();
		_client.AddGroupHandler = (parent, name) =>
		{
			ServerChanged(() => _client.Directories =
			[
				_b.Directory("d1", "Repo", [_b.Group("g1", "Group A", _b.Session("s2", "In group")), _b.Group("g9", name)], _b.Session("s1", "Top session", workers: 2), _b.Session("s4", "Fourth")),
				_client.Directories[1],
			]);
			return new TreeAddGroupResult("g9");
		};
		Answer(d =>
		{
			d.InputText = " Fresh ";
			Accept(d);
		});

		await tree.Operations!.AddGroupAsync(tree.FindNode("s4"));

		Assert.Contains("tree.addGroup:d1:Fresh", _client.Calls);
		Assert.Equal("g9", tree.SelectedNodeId);
		Assert.Contains(tree.Rows, r => r.Id == "g9");
	}

	[Fact]
	public async Task NewGroup_IsNotOfferedForAWorker()
	{
		var tree = await LoadedTreeAsync();
		var worker = new TreeNodeViewModel("w1", TreeNodeKind.Worker, "Worker", tree.FindNode("s1"));

		Assert.Null(TreeOperations.ContainerOf(worker));
		await tree.Operations!.AddGroupAsync(worker);
		Assert.Empty(_presenter.Shown);
	}

	[Fact]
	public async Task AddDirectory_UsesTheClientsFolderDialogAndSelectsTheNewDirectory()
	{
		var tree = await LoadedTreeAsync();
		_folders.Answer = @"C:\work\new";
		_client.AddDirectoryHandler = path =>
		{
			ServerChanged(() => _client.Directories = [.. _client.Directories, _b.Directory("d7", "new", [])]);
			return new TreeAddDirectoryResult("d7", "new");
		};

		await tree.Operations!.AddDirectoryAsync();

		Assert.Contains(@"tree.addDirectory:C:\work\new", _client.Calls);
		Assert.Equal("d7", tree.SelectedNodeId);
		Assert.Contains(tree.Rows, r => r.Id == "d7");
	}

	[Fact]
	public async Task AddDirectory_RefusedAsTooBroad_ShowsTheDaemonsMessage()
	{
		var tree = await LoadedTreeAsync();
		_folders.Answer = @"C:\";
		_client.AddDirectoryHandler = _ => throw new TessynRpcException(-32000, "That folder cannot be added");

		await tree.Operations!.AddDirectoryAsync();

		var message = Assert.Single(_presenter.Shown);
		Assert.Contains("That folder cannot be added", message.Message);
		Assert.Equal(Constants.TreeTexts.AddDirectoryFailed, message.Title);
	}

	[Fact]
	public async Task AddDirectory_CancelledPicker_CallsNothing()
	{
		var tree = await LoadedTreeAsync();
		_folders.Answer = null;

		await tree.Operations!.AddDirectoryAsync();

		Assert.Equal(1, _folders.Asked);
		Assert.DoesNotContain(_client.Calls, c => c.StartsWith("tree.addDirectory", StringComparison.Ordinal));
	}

	// ---------------------------------------------------------------- move by menu

	[Fact]
	public async Task MoveTo_ListsTheAllowedPlacesWithTheirPathsAndMovesToTheChosenOne()
	{
		var tree = await LoadedTreeAsync();
		_client.MoveTargetsHandler = _ => new TreeMoveTargetsResult(["d1", "g1", "d2"]);
		Answer(d =>
		{
			Assert.True(d.IsChoice);
			Assert.Equal(["Repo / Group A", "Other"], d.Choices.Select(c => c.Label));   // the current parent (Repo) is not offered
			d.ChooseCommand.Execute(d.Choices[0]);
		});

		await tree.Operations!.MoveToAsync(tree.FindNode("s4"));

		Assert.Contains("tree.moveTargets:s4", _client.Calls);
		Assert.Contains("tree.moveNode:s4:g1:", _client.Calls);
	}

	[Fact]
	public async Task MoveTo_WithNowhereToGo_SaysSoInsteadOfOpeningAnEmptyList()
	{
		var tree = await LoadedTreeAsync();
		_client.MoveTargetsHandler = _ => new TreeMoveTargetsResult(["d1"]);

		await tree.Operations!.MoveToAsync(tree.FindNode("s4"));

		var message = Assert.Single(_presenter.Shown);
		Assert.True(message.IsConfirm);
		Assert.Equal(Constants.TreeTexts.NowhereToMove, message.Message);
		Assert.DoesNotContain(_client.Calls, c => c.StartsWith("tree.moveNode", StringComparison.Ordinal));
	}

	[Fact]
	public async Task MoveTo_RefusedByTheDaemon_ShowsItsReason()
	{
		var tree = await LoadedTreeAsync();
		_client.MoveTargetsHandler = _ => new TreeMoveTargetsResult(["d2"]);
		_client.MoveNodeHandler = (_, _, _) => throw new TessynRpcException(-32000, "Cannot move here: the target is in a different git repository");
		Answer(d =>
		{
			if (d.IsChoice)
				d.ChooseCommand.Execute(d.Choices[0]);
			else
				d.CancelCommand.Execute(null);
		});

		await tree.Operations!.MoveToAsync(tree.FindNode("s4"));

		Assert.Contains("different git repository", _presenter.Shown.Last().Message);
	}

	[Fact]
	public async Task MoveTo_IsOnlyForTopLevelSessions()
	{
		var tree = await LoadedTreeAsync();

		Assert.True(TreeOperations.IsMovable(tree.FindNode("s1")));
		Assert.True(TreeOperations.IsMovable(tree.FindNode("s2")));
		Assert.False(TreeOperations.IsMovable(tree.FindNode("g1")));
		Assert.False(TreeOperations.IsMovable(tree.FindNode("d1")));
		Assert.False(TreeOperations.IsMovable(new TreeNodeViewModel("w1", TreeNodeKind.Worker, "W", tree.FindNode("s1"))));
	}

	// ---------------------------------------------------------------- drag and drop

	[Fact]
	public async Task Drag_AllowsATargetTheDaemonListedAndRefusesAnotherRepository()
	{
		var tree = await LoadedTreeAsync();
		_client.MoveTargetsHandler = _ => new TreeMoveTargetsResult(["d1", "g1"]);
		var ops = tree.Operations!;
		var source = tree.FindNode("s4")!;

		await ops.BeginDrag(source);
		var onGroup = ops.Evaluate(source, tree.FindNode("g1"));
		var onOtherRepository = ops.Evaluate(source, tree.FindNode("d2"));
		var onSessionOfAnotherRepository = ops.Evaluate(source, tree.FindNode("s3"));
		var onSessionOfTheGroup = ops.Evaluate(source, tree.FindNode("s2"));

		Assert.True(onGroup.Allowed);
		Assert.Same(tree.FindNode("g1"), onGroup.Target);
		Assert.False(onOtherRepository.Allowed);
		Assert.Equal(Constants.TreeTexts.OtherRepository, onOtherRepository.Reason);
		Assert.False(onSessionOfAnotherRepository.Allowed);   // a session stands for its container
		Assert.True(onSessionOfTheGroup.Allowed);
		Assert.Same(tree.FindNode("g1"), onSessionOfTheGroup.Target);
		Assert.Equal(1, _client.Calls.Count(c => c == "tree.moveTargets:s4"));   // asked once for the whole drag
	}

	[Fact]
	public async Task Drag_BeforeTheDaemonAnswered_RefusesWithTheCheckingText()
	{
		var tree = await LoadedTreeAsync();
		var source = tree.FindNode("s4")!;

		var decision = tree.Operations!.Evaluate(source, tree.FindNode("g1"));

		Assert.False(decision.Allowed);
		Assert.Equal(Constants.TreeTexts.MoveTargetsPending, decision.Reason);
	}

	[Fact]
	public async Task Drag_WhenTheDaemonCannotAnswer_RefusesEverything()
	{
		var tree = await LoadedTreeAsync();
		_client.MoveTargetsHandler = _ => throw new TessynRpcException(-32601, "Method not found");
		var source = tree.FindNode("s4")!;

		await Assert.ThrowsAsync<TessynRpcException>(() => tree.Operations!.BeginDrag(source));

		Assert.Equal(Constants.TreeTexts.MoveTargetsFailed, tree.Operations!.Evaluate(source, tree.FindNode("g1")).Reason);
	}

	[Fact]
	public async Task Drag_RefusesARunningSessionAndNonSessions()
	{
		var tree = await LoadedTreeAsync();
		_client.Directories =
		[
			_b.Directory("d1", "Repo", [], _b.Session("run", "Running", running: true), _b.Session("s4", "Fourth")),
		];
		_client.TreeVersion++;
		await tree.RefreshAsync();
		_client.MoveTargetsHandler = _ => new TreeMoveTargetsResult(["d1"]);
		var ops = tree.Operations!;

		await ops.BeginDrag(tree.FindNode("run")!);
		Assert.Equal(Constants.TreeTexts.SessionRunning, ops.Evaluate(tree.FindNode("run")!, tree.FindNode("d1")).Reason);

		Assert.Equal(Constants.TreeTexts.NotMovable, ops.Evaluate(tree.FindNode("d1")!, tree.FindNode("d1")).Reason);
		Assert.Equal(Constants.TreeTexts.DropOnContainer, ops.Evaluate(tree.FindNode("s4")!, null).Reason);
	}

	[Fact]
	public async Task Drop_OnAnAllowedGroup_MovesAndShowsTheSessionThere()
	{
		var tree = await LoadedTreeAsync();
		_client.MoveTargetsHandler = _ => new TreeMoveTargetsResult(["d1", "g1"]);
		_client.MoveNodeHandler = (id, parent, index) =>
		{
			ServerChanged(() => _client.Directories =
			[
				_b.Directory("d1", "Repo", [_b.Group("g1", "Group A", _b.Session("s4", "Fourth"), _b.Session("s2", "In group"))], _b.Session("s1", "Top session", workers: 2)),
				_client.Directories[1],
			]);
			return new TreeMoveResult(true);
		};
		var ops = tree.Operations!;
		var source = tree.FindNode("s4")!;
		await ops.BeginDrag(source);

		await ops.DropAsync(source, tree.FindNode("g1"));

		Assert.Contains("tree.moveNode:s4:g1:", _client.Calls);
		Assert.Equal(["d1", "g1", "s4", "s2", "s1", "d2"], tree.Rows.Select(r => r.Id));
		Assert.Equal("g1", tree.FindNode("s4")!.Parent!.Id);
	}

	[Fact]
	public async Task Drop_OnARefusedTarget_MovesNothingAndSaysWhy()
	{
		var tree = await LoadedTreeAsync();
		_client.MoveTargetsHandler = _ => new TreeMoveTargetsResult(["d1", "g1"]);
		var ops = tree.Operations!;
		var source = tree.FindNode("s4")!;
		await ops.BeginDrag(source);

		await ops.DropAsync(source, tree.FindNode("d2"));

		Assert.DoesNotContain(_client.Calls, c => c.StartsWith("tree.moveNode", StringComparison.Ordinal));
		Assert.Equal(Constants.TreeTexts.OtherRepository, tree.StatusText);
	}

	[Fact]
	public async Task Drop_OnTheCurrentContainer_IsANoOp()
	{
		var tree = await LoadedTreeAsync();
		_client.MoveTargetsHandler = _ => new TreeMoveTargetsResult(["d1", "g1"]);
		var ops = tree.Operations!;
		var source = tree.FindNode("s4")!;
		await ops.BeginDrag(source);

		await ops.DropAsync(source, tree.FindNode("s1"));   // a sibling: the container is where it already is

		Assert.DoesNotContain(_client.Calls, c => c.StartsWith("tree.moveNode", StringComparison.Ordinal));
	}

	[Fact]
	public async Task Drag_AsksAgainOnTheNextDrag()
	{
		var tree = await LoadedTreeAsync();
		_client.MoveTargetsHandler = _ => new TreeMoveTargetsResult(["d1"]);
		var ops = tree.Operations!;
		var source = tree.FindNode("s4")!;

		await ops.BeginDrag(source);
		ops.EndDrag();
		await ops.BeginDrag(source);

		Assert.Equal(2, _client.Calls.Count(c => c == "tree.moveTargets:s4"));
	}

	// ---------------------------------------------------------------- git origins, tooltips

	[Fact]
	public async Task Load_FillsTheGitOriginLabelAndTooltipOfEveryDirectoryWithOneCall()
	{
		_client.NodeInfoHandler = ids => new TreeNodeInfoResult(
		[
			Info("d1", "directory", "", @"C:\src\repo", "https://github.com/acme/repo.git", "Repo"),
			Info("d2", "directory", "", @"C:\src\other", null, "Other"),
		]);
		var tree = CreateTree();

		await tree.LoadAsync();

		Assert.Equal(["tree.nodeInfo:d1,d2"], _client.Calls.Where(c => c.StartsWith("tree.nodeInfo", StringComparison.Ordinal)));
		var repo = tree.FindNode("d1")!;
		Assert.Equal("acme/repo", repo.GitOriginLabel);
		Assert.True(repo.HasGitOrigin);
		Assert.Contains(@"C:\src\repo", repo.ToolTipText);
		Assert.Contains("https://github.com/acme/repo.git", repo.ToolTipText);
		Assert.False(tree.FindNode("d2")!.HasGitOrigin);
	}

	[Fact]
	public async Task Tooltip_OfASessionIsLoadedOnFirstUseOnly()
	{
		_client.NodeInfoHandler = ids => new TreeNodeInfoResult(ids.Select(id => id == "s1"
			? Info("s1", "session", "Top session", @"C:\src\repo", "git@github.com:acme/repo.git", "Repo")
			: Info(id, "directory", "", @"C:\src\" + id, null, id)).ToList());
		var tree = await LoadedTreeAsync();
		var session = tree.FindNode("s1")!;
		Assert.Equal("Top session", session.ToolTipText);   // the label until asked

		await tree.EnsureInfoAsync(session);
		await tree.EnsureInfoAsync(session);

		Assert.Equal(1, _client.Calls.Count(c => c == "tree.nodeInfo:s1"));
		Assert.Contains(@"C:\src\repo", session.ToolTipText);
		Assert.Contains("Top session", session.ToolTipText);
		Assert.False(session.HasGitOrigin);   // the label beside a row is for directories
	}

	[Fact]
	public async Task NodeInfo_FailingOnAnOlderDaemon_DoesNotBreakTheTree()
	{
		_client.NodeInfoHandler = _ => throw new TessynRpcException(-32601, "Method not found");
		var tree = CreateTree();

		await tree.LoadAsync();

		Assert.Equal(["d1", "d2"], tree.Rows.Select(r => r.Id));
		Assert.Equal("Repo", tree.FindNode("d1")!.ToolTipText);
	}

	[Fact]
	public async Task RefreshGitOrigins_StartsTheScan_AndTheLabelsFollowOnTheDaemonsTreeChanged()
	{
		var origin = (string?)null;
		_client.NodeInfoHandler = ids => new TreeNodeInfoResult(ids.Select(id => Info(id, "directory", "", @"C:\" + id, id == "d1" ? origin : null, id)).ToList());
		var tree = CreateTree();
		await tree.LoadAsync();
		Assert.False(tree.FindNode("d1")!.HasGitOrigin);

		await tree.Operations!.RefreshGitOriginsAsync();
		origin = "https://github.com/acme/repo.git";
		_client.RaiseNotification("tree.changed", new TreeChangedPayload(_client.TreeVersion));   // same version: only the origins changed
		await tree.WhenIdleAsync();

		Assert.Contains("tree.refreshGitOrigins:", _client.Calls);
		Assert.Equal("acme/repo", tree.FindNode("d1")!.GitOriginLabel);
	}

	[Fact]
	public async Task RefreshGitOrigins_WhileOneRuns_SaysSo()
	{
		var tree = await LoadedTreeAsync();
		_client.RefreshGitOriginsHandler = () => new TreeRefreshGitOriginsResult(false);

		await tree.Operations!.RefreshGitOriginsAsync();

		Assert.Equal(Constants.TreeTexts.RefreshAlreadyRunning, tree.StatusText);
	}

	// ---------------------------------------------------------------- live update

	[Fact]
	public async Task TreeChangedFromAnotherClient_UpdatesTheRowsWithoutAManualRefresh()
	{
		var tree = await LoadedTreeAsync();
		ServerChanged(() => _client.Directories = [_client.Directories[0], _b.Directory("d2", "Other", [], _b.Session("s3", "Renamed elsewhere"))]);

		_client.RaiseNotification("tree.changed", new TreeChangedPayload(_client.TreeVersion));
		await tree.WhenIdleAsync();

		Assert.Equal("Renamed elsewhere", tree.FindNode("s3")!.Label);
	}

	[Fact]
	public async Task Selection_SurvivesAMutationRebuild()
	{
		var tree = await LoadedTreeAsync();
		tree.SelectNodeById("s4");
		ServerChanged(() => { });

		await tree.RefreshAsync();

		Assert.Equal("s4", tree.SelectedNode?.Id);
	}

	// ---------------------------------------------------------------- toolbar commands

	[Fact]
	public async Task ToolbarCommands_FollowTheSelection()
	{
		var tree = await LoadedTreeAsync();

		tree.SelectedNode = tree.FindNode("d1");
		Assert.True(tree.AddGroupCommand.CanExecute(null));
		Assert.False(tree.RenameCommand.CanExecute(null));   // a directory
		Assert.False(tree.MoveToCommand.CanExecute(null));
		Assert.True(tree.DeleteCommand.CanExecute(null));

		tree.SelectedNode = tree.FindNode("s4");
		Assert.True(tree.RenameCommand.CanExecute(null));
		Assert.True(tree.MoveToCommand.CanExecute(null));
		Assert.True(tree.AddDirectoryCommand.CanExecute(null));
		Assert.True(tree.RefreshGitOriginsCommand.CanExecute(null));
	}

	[Fact]
	public void ATreeWithoutOperations_DisablesEveryToolbarCommand()
	{
		_client.Directories = [_b.Directory("d1", "Repo", [])];
		using var tree = new TreeViewModel(_client, new ImmediateUiDispatcher(), NullLogger<TreeViewModel>.Instance, TimeSpan.Zero);

		Assert.Null(tree.Operations);
		Assert.False(tree.AddDirectoryCommand.CanExecute(null));
		Assert.False(tree.RefreshGitOriginsCommand.CanExecute(null));
	}

	// ---------------------------------------------------------------- git origin text

	[Theory]
	[InlineData("https://github.com/acme/repo.git", "acme/repo")]
	[InlineData("git@github.com:acme/repo.git", "acme/repo")]
	[InlineData("https://dev.azure.com/org/proj/_git/repo", "_git/repo")]
	[InlineData("ssh://git@host/acme/repo/", "acme/repo")]
	[InlineData("repo", "repo")]
	public void GitOriginText_ShortensToOwnerAndRepo(string origin, string expected) =>
		Assert.Equal(expected, GitOriginText.Short(origin));
}
