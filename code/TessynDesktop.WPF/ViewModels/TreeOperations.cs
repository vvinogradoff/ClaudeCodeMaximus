using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Services;
using static TessynDesktop.WPF.Constants;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// What the user can do to the tree: add a directory or group, rename, delete (explaining why the daemon refused), move by menu or by drag, refresh git
/// origins. Every change goes through the daemon (<c>tree.*</c>); the tree then rebuilds from its snapshot, so the result shows at once and the daemon's
/// own <c>tree.changed</c> only confirms it. A refusal is shown to the user with the daemon's message. Contains no WPF types.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TreeOperations
{
	private readonly ITessynClient _client;
	private readonly IDialogService _dialogs;
	private readonly IFolderPicker _folders;
	private readonly ILogger<TreeOperations> _logger;
	private TreeViewModel? _tree;

	private string? _dragSourceId;
	private Task<HashSet<string>>? _dragTargets;

	public TreeOperations(ITessynClient client, IDialogService dialogs, IFolderPicker folders, ILogger<TreeOperations> logger)
	{
		_client = client;
		_dialogs = dialogs;
		_folders = folders;
		_logger = logger;
	}

	/// <summary>Binds the operations to the tree they act on (done by the tree itself).</summary>
	public void Attach(TreeViewModel tree) => _tree = tree;

	/// <summary>The directory or group a new group goes under for this node: the node itself, or the parent of a session; null when none applies.</summary>
	public static TreeNodeViewModel? ContainerOf(TreeNodeViewModel? node) => node?.Kind switch
	{
		TreeNodeKind.Directory or TreeNodeKind.Group => node,
		TreeNodeKind.Session when node.Parent is { Kind: TreeNodeKind.Directory or TreeNodeKind.Group } parent => parent,
		_ => null,
	};

	/// <summary>Only a top-level session (not a worker, not a search-result row) can be moved.</summary>
	public static bool IsMovable(TreeNodeViewModel? node) => node is { Kind: TreeNodeKind.Session, Parent.Kind: TreeNodeKind.Directory or TreeNodeKind.Group };

	/// <summary>Renaming is for sessions and groups (the daemon refuses a directory).</summary>
	public static bool CanRename(TreeNodeViewModel? node) => node is { Kind: not TreeNodeKind.Directory };

	public async Task AddDirectoryAsync()
	{
		var path = await _folders.PickAsync(TreeTexts.AddDirectoryTitle);
		if (string.IsNullOrWhiteSpace(path))
			return;

		await RunAsync(TreeTexts.AddDirectoryFailed, async () =>
		{
			var added = await _client.TreeAddDirectoryAsync(path);
			await RefreshAsync();
			_tree?.SelectNodeById(added.DirectoryId);
		});
	}

	public async Task AddGroupAsync(TreeNodeViewModel? near)
	{
		var container = ContainerOf(near);
		if (container == null)
			return;

		var name = await _dialogs.InputAsync(new InputDialogRequest(TreeTexts.NewGroupTitle, TreeTexts.NewGroupPrompt, string.Empty, Validate: RequireName));
		if (name == null)
			return;

		await RunAsync(TreeTexts.NewGroupFailed, async () =>
		{
			var added = await _client.TreeAddGroupAsync(container.Id, name.Trim());
			await RefreshAsync();
			_tree?.SelectNodeById(added.GroupId);
		});
	}

	public async Task RenameAsync(TreeNodeViewModel? node)
	{
		if (!CanRename(node))
			return;

		var name = await _dialogs.InputAsync(new InputDialogRequest(TreeTexts.RenameTitle, TreeTexts.RenamePrompt, node!.Label, Validate: RequireName));
		if (name == null || name.Trim() == node.Label)
			return;

		await RunAsync(TreeTexts.RenameFailed, async () =>
		{
			await _client.TreeRenameNodeAsync(node.Id, name.Trim());
			await RefreshAsync();
		});
	}

	/// <summary>
	/// Deletes a node. The daemon is asked without force first: it only deletes what holds nothing. When it refuses because the session has a transcript file or
	/// workers, the user is asked with that reason and the delete is repeated with force; a running or non-empty node is only explained.
	/// </summary>
	public async Task DeleteAsync(TreeNodeViewModel? node)
	{
		if (node == null)
			return;

		await RunAsync(TreeTexts.DeleteFailed, async () =>
		{
			var result = await _client.TreeDeleteNodeAsync(node.Id, force: false);
			if (!result.Deleted)
				result = await DeleteBlockedAsync(node, result.BlockedBy);

			if (result.Deleted)
				await RefreshAsync();
		});
	}

	private async Task<TreeDeleteResult> DeleteBlockedAsync(TreeNodeViewModel node, string? blockedBy)
	{
		var refused = new TreeDeleteResult(false, blockedBy);
		switch (blockedBy)
		{
			case RemoteProtocol.TreeMutation.BlockedBy.SessionFile or RemoteProtocol.TreeMutation.BlockedBy.Workers:
				var message = string.Format(blockedBy == RemoteProtocol.TreeMutation.BlockedBy.Workers ? TreeTexts.BlockedWorkers : TreeTexts.BlockedSessionFile, node.Label);
				var confirmed = await _dialogs.ConfirmAsync(new ConfirmDialogRequest(TreeTexts.DeleteTitle, message, TreeTexts.DeleteButton, IsDestructive: true));
				return confirmed ? await _client.TreeDeleteNodeAsync(node.Id, force: true) : refused;
			case RemoteProtocol.TreeMutation.BlockedBy.Running:
				await ShowAsync(TreeTexts.DeleteTitle, string.Format(TreeTexts.BlockedRunning, node.Label));
				return refused;
			case RemoteProtocol.TreeMutation.BlockedBy.NotEmpty:
				await ShowAsync(TreeTexts.DeleteTitle, string.Format(TreeTexts.BlockedNotEmpty, node.Label));
				return refused;
			default:
				await ShowAsync(TreeTexts.DeleteTitle, string.Format(TreeTexts.BlockedUnknown, node.Label, blockedBy ?? "no reason given"));
				return refused;
		}
	}

	/// <summary>"Move to…": lists the places the daemon allows (the git-origin rule) and moves the session to the chosen one.</summary>
	public async Task MoveToAsync(TreeNodeViewModel? node)
	{
		if (!IsMovable(node) || _tree == null)
			return;

		await RunAsync(TreeTexts.MoveFailed, async () =>
		{
			var targets = await _client.TreeMoveTargetsAsync(node!.Id);
			var choices = targets.ParentIds
				.Where(id => id != node.Parent!.Id)
				.Select(id => _tree.FindNode(id))
				.OfType<TreeNodeViewModel>()
				.Select(target => new DialogChoice(target.Id, PathLabel(target)))
				.ToList();
			if (choices.Count == 0)
			{
				await ShowAsync(TreeTexts.MoveTitle, TreeTexts.NowhereToMove);
				return;
			}

			var chosen = await _dialogs.ChoiceAsync(new ChoiceDialogRequest(TreeTexts.MoveTitle, string.Format(TreeTexts.MoveMessage, node.Label), choices));
			if (chosen != null)
				await MoveAsync(node, chosen);
		});
	}

	/// <summary>Moves a session into a directory or group (to the first position there), then shows it in its new place.</summary>
	public async Task MoveAsync(TreeNodeViewModel source, string targetId)
	{
		var moved = await _client.TreeMoveNodeAsync(source.Id, targetId);
		if (!moved.Moved)
		{
			await ShowAsync(TreeTexts.MoveFailed, TreeTexts.MoveFailed);
			return;
		}

		await RefreshAsync();
		_tree?.RevealNode(source.Id);
	}

	/// <summary>Starts the daemon's background git-origin scan; the labels follow when it reports <c>tree.changed</c>.</summary>
	public async Task RefreshGitOriginsAsync()
	{
		await RunAsync(TreeTexts.RefreshFailed, async () =>
		{
			var result = await _client.TreeRefreshGitOriginsAsync();
			if (!result.Started && _tree != null)
				_tree.StatusText = TreeTexts.RefreshAlreadyRunning;
		});
	}

	/// <summary>
	/// Starts asking where a session may go, so the answer is there while the pointer travels (the drop feedback cannot wait for a call).
	/// The returned task completes with the answer; the view ignores it, tests await it.
	/// </summary>
	public Task BeginDrag(TreeNodeViewModel source)
	{
		if (!IsMovable(source))
		{
			EndDrag();
			return Task.CompletedTask;
		}

		if (_dragSourceId == source.Id && _dragTargets != null)
			return _dragTargets;

		_dragSourceId = source.Id;
		_dragTargets = LoadTargetsAsync(source.Id);
		return _dragTargets;
	}

	/// <summary>Forgets the answer: the rule may change (a git-origin refresh), so each drag asks again.</summary>
	public void EndDrag()
	{
		_dragSourceId = null;
		_dragTargets = null;
	}

	private async Task<HashSet<string>> LoadTargetsAsync(string nodeId)
	{
		try
		{
			return new HashSet<string>((await _client.TreeMoveTargetsAsync(nodeId)).ParentIds);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "tree.moveTargets failed for {NodeId}", nodeId);
			throw;
		}
	}

	/// <summary>Whether <paramref name="source"/> may be dropped on <paramref name="over"/> (a directory, a group, or a session standing for its container).</summary>
	public DropDecision Evaluate(TreeNodeViewModel source, TreeNodeViewModel? over)
	{
		if (!IsMovable(source))
			return DropDecision.Refuse(TreeTexts.NotMovable);

		var target = ContainerOf(over);
		if (target == null)
			return DropDecision.Refuse(TreeTexts.DropOnContainer);

		if (source.IsRunning)
			return DropDecision.Refuse(TreeTexts.SessionRunning);

		if (_dragSourceId != source.Id || _dragTargets == null || !_dragTargets.IsCompleted)
			return DropDecision.Refuse(TreeTexts.MoveTargetsPending);

		if (_dragTargets.IsFaulted)
		{
			_ = _dragTargets.Exception;
			return DropDecision.Refuse(TreeTexts.MoveTargetsFailed);
		}

		return _dragTargets.IsCompletedSuccessfully && _dragTargets.Result.Contains(target.Id)
			? new DropDecision(true, null, target)
			: DropDecision.Refuse(TreeTexts.OtherRepository);
	}

	/// <summary>Drops a session: moves it when the rule allows, does nothing when it is already there, otherwise tells the user why not.</summary>
	public async Task DropAsync(TreeNodeViewModel source, TreeNodeViewModel? over)
	{
		var decision = Evaluate(source, over);
		EndDrag();
		if (!decision.Allowed)
		{
			if (_tree != null)
				_tree.StatusText = decision.Reason;
			return;
		}

		if (decision.Target == source.Parent)
			return;

		await RunAsync(TreeTexts.MoveFailed, () => MoveAsync(source, decision.Target!.Id));
	}

	/// <summary>"Directory / Group / Subgroup": where a container sits, so two groups of the same name can be told apart.</summary>
	public static string PathLabel(TreeNodeViewModel container)
	{
		var parts = new List<string>();
		for (var node = container; node != null; node = node.Parent)
			parts.Add(node.Label);

		parts.Reverse();
		return string.Join(" / ", parts);
	}

	private static string? RequireName(string text) => string.IsNullOrWhiteSpace(text) ? TreeTexts.NameRequired : null;

	private Task RefreshAsync() => _tree?.RefreshAsync() ?? Task.CompletedTask;

	private Task ShowAsync(string title, string message) =>
		_dialogs.ConfirmAsync(new ConfirmDialogRequest(title, message, TreeTexts.Ok, TreeTexts.Close));

	/// <summary>Runs an operation; a refusal or failure is logged and shown with its message (the daemon's reason for a refusal).</summary>
	private async Task RunAsync(string title, Func<Task> operation)
	{
		try
		{
			await operation();
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogWarning(ex, "{Operation} failed", title);
			await ShowAsync(title, ex.Message);
		}
	}
}
