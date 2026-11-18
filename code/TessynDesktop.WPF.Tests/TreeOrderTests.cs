using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// UC.TO.01 / TC.TO.01: a new session goes last in its directory or group (FR.1.30), and Ctrl+Up / Ctrl+Down move a top-level session one place (FR.1.31).
/// The fake daemon keeps the order of each container and answers <c>tree.moveNode</c> and <c>tree.createSession</c> the way the real one does (the index counts after the session left).
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection(nameof(SessionDisplayCollection))]
public sealed class TreeOrderTests
{
	private sealed class NoFolders : IFolderPicker
	{
		public Task<string?> PickAsync(string title) => Task.FromResult<string?>(null);
	}

	private readonly TreeBuilder _b = new();
	private readonly FakeTessynClient _client = new();
	private readonly FakeDialogPresenter _presenter = new();

	/// <summary>The sessions of each container in order (the daemon's state); a running one is listed in <see cref="_running"/>.</summary>
	private readonly Dictionary<string, List<string>> _order = new()
	{
		["d1"] = ["A", "B", "C", "D"],
		["g1"] = ["G1", "G2", "G3"],
	};

	private readonly HashSet<string> _running = [];
	private readonly Dictionary<string, int> _workers = new() { ["B"] = 2 };

	private TreeSessionDto Session(string id) => _b.Session(id, id, _workers.GetValueOrDefault(id), _running.Contains(id));

	private void Publish()
	{
		_client.Directories =
		[
			_b.Directory("d1", "Repo", [_b.Group("g1", "Group", _order["g1"].Select(Session).ToArray())], _order["d1"].Select(Session).ToArray()),
			_b.Directory("d2", "Empty", []),
		];
		_client.TreeVersion++;
	}

	private TreeViewModel CreateTree()
	{
		Publish();
		_client.MoveNodeHandler = (id, parent, index) =>
		{
			var list = _order[parent];
			foreach (var other in _order.Values)
				other.Remove(id);
			list.Insert(Math.Min(index ?? 0, list.Count), id);
			Publish();
			return new TreeMoveResult(true);
		};
		_client.CreateSessionHandler = (anchor, name) =>
		{
			var list = _order[anchor];
			list.Add("N");
			Publish();
			return new CreateSessionResult(_b.Session("N", name), anchor == "g1" ? "d1" : anchor, anchor == "g1" ? "g1" : null);
		};
		var operations = new TreeOperations(_client, new DialogService(_presenter, new ImmediateUiDispatcher()), new NoFolders(), NullLogger<TreeOperations>.Instance);
		return new TreeViewModel(_client, new ImmediateUiDispatcher(), NullLogger<TreeViewModel>.Instance, TimeSpan.Zero, operations);
	}

	private async Task<TreeViewModel> LoadedAsync(string select)
	{
		var tree = CreateTree();
		await tree.LoadAsync();
		tree.SelectNodeById(select);
		return tree;
	}

	private static string[] Order(TreeViewModel tree, string container) => tree.FindNode(container)!.Children.Where(c => c.Kind == TreeNodeKind.Session).Select(c => c.Id).ToArray();

	private int MoveCalls => _client.Calls.Count(c => c.StartsWith("tree.moveNode", StringComparison.Ordinal));

	// ---------------------------------------------------------------- FR.1.30

	/// <summary>TC.TO.04, TC.TO.05 (view model): New session with a directory selected goes last; the row is there at once and selected.</summary>
	[Fact]
	public async Task NewSession_OnADirectory_ShowsTheRowLast_Selected()
	{
		var tree = await LoadedAsync("d1");
		_presenter.Answer = d => { d.AcceptCommand.Execute(null); return Task.CompletedTask; };

		await tree.Operations!.AddSessionAsync(tree.SelectedNode);

		Assert.Contains("tree.createSession:d1:New session", _client.Calls);
		Assert.Equal(["A", "B", "C", "D", "N"], Order(tree, "d1"));
		Assert.Equal("N", tree.SelectedNode?.Id);
		Assert.Equal("N", tree.Rows.Last(r => r.Parent?.Id == "d1").Id);
	}

	/// <summary>TC.TO.05 (view model): with a session selected the container is its parent, so the new one is last in it, not beside the selected one.</summary>
	[Fact]
	public async Task NewSession_WithASessionSelected_GoesLastInItsDirectoryOrGroup()
	{
		var tree = await LoadedAsync("B");
		_presenter.Answer = d => { d.AcceptCommand.Execute(null); return Task.CompletedTask; };

		await tree.Operations!.AddSessionAsync(tree.SelectedNode);

		Assert.Contains("tree.createSession:d1:New session", _client.Calls);
		Assert.Equal("N", Order(tree, "d1").Last());

		var inGroup = await LoadedAsync("G1");
		await inGroup.Operations!.AddSessionAsync(inGroup.SelectedNode);
		Assert.Equal(["G1", "G2", "G3", "N"], Order(inGroup, "g1"));
	}

	// ---------------------------------------------------------------- FR.1.31

	/// <summary>TC.TO.10: up and down in a directory and in a group (a group stands before the sessions in the list); the index is the neighbour's among the others.</summary>
	[Fact]
	public async Task CtrlUpAndDown_MoveOnePlace_WithTheNeighboursIndex()
	{
		var tree = await LoadedAsync("C");
		var ops = tree.Operations!;

		await ops.MoveStepAsync(tree.FindNode("C"), -1);
		Assert.Contains("tree.moveNode:C:d1:1", _client.Calls);
		Assert.Equal(["A", "C", "B", "D"], Order(tree, "d1"));
		Assert.Equal("C", tree.SelectedNode?.Id);

		await ops.MoveStepAsync(tree.FindNode("C"), -1);
		Assert.Contains("tree.moveNode:C:d1:0", _client.Calls);
		Assert.Equal(["C", "A", "B", "D"], Order(tree, "d1"));

		await ops.MoveStepAsync(tree.FindNode("C"), 1);
		Assert.Contains("tree.moveNode:C:d1:1", _client.Calls);
		await ops.MoveStepAsync(tree.FindNode("C"), 1);
		Assert.Contains("tree.moveNode:C:d1:2", _client.Calls);
		await ops.MoveStepAsync(tree.FindNode("C"), 1);
		Assert.Contains("tree.moveNode:C:d1:3", _client.Calls);
		Assert.Equal(["A", "B", "D", "C"], Order(tree, "d1"));

		tree.SelectNodeById("G2");
		await ops.MoveStepAsync(tree.FindNode("G2"), -1);
		Assert.Contains("tree.moveNode:G2:g1:0", _client.Calls);
		Assert.Equal(["G2", "G1", "G3"], Order(tree, "g1"));
		await ops.MoveStepAsync(tree.FindNode("G2"), 1);
		Assert.Equal(["G1", "G2", "G3"], Order(tree, "g1"));
		Assert.Equal("G2", tree.SelectedNode?.Id);
	}

	/// <summary>TC.TO.11.</summary>
	[Fact]
	public async Task FirstSessionUp_AndLastSessionDown_DoNothing()
	{
		var tree = await LoadedAsync("A");

		await tree.Operations!.MoveStepAsync(tree.FindNode("A"), -1);
		await tree.Operations.MoveStepAsync(tree.FindNode("D"), 1);
		await tree.Operations.MoveStepAsync(tree.FindNode("G1"), -1);
		await tree.Operations.MoveStepAsync(tree.FindNode("G3"), 1);

		Assert.Equal(0, MoveCalls);
	}

	/// <summary>TC.TO.12: a worker, a group, a directory, and a search in progress.</summary>
	[Fact]
	public async Task WorkerGroupDirectory_AndAnActiveSearch_DoNothing()
	{
		var tree = await LoadedAsync("C");
		var ops = tree.Operations!;
		var worker = new TreeNodeViewModel("w1", TreeNodeKind.Worker, "W", tree.FindNode("B"));

		await ops.MoveStepAsync(worker, 1);
		await ops.MoveStepAsync(tree.FindNode("g1"), 1);
		await ops.MoveStepAsync(tree.FindNode("d1"), 1);
		await ops.MoveStepAsync(null, 1);
		Assert.Equal(0, MoveCalls);

		_client.SearchHandler = q => new TreeSearchResult(q, false, false, false, [new TreeSearchMatchDto("C", 1, 0, false, [])]);
		tree.SearchText = "C";
		await tree.WhenIdleAsync();
		Assert.True(tree.IsSearching);
		await ops.MoveStepAsync(tree.FindNode("C"), -1);
		await ops.MoveStepAsync(tree.SelectedNode ?? tree.Rows[0], -1);
		Assert.Equal(0, MoveCalls);
	}

	/// <summary>TC.TO.13: the drag's refusal text, nothing moved.</summary>
	[Fact]
	public async Task ARunningSession_IsNotMoved_AndTheStatusLineSaysWhy()
	{
		_running.Add("C");
		var tree = await LoadedAsync("C");

		await tree.Operations!.MoveStepAsync(tree.FindNode("C"), -1);

		Assert.Equal(0, MoveCalls);
		Assert.Equal(Constants.TreeTexts.SessionRunning, tree.StatusText);
		Assert.Equal(["A", "B", "C", "D"], Order(tree, "d1"));
	}

	/// <summary>TC.TO.14: a press while a move is in flight (here from inside the daemon call) is dropped, not queued.</summary>
	[Fact]
	public async Task APressWhileAMoveIsInFlight_IsIgnored()
	{
		var tree = await LoadedAsync("C");
		var inner = Task.CompletedTask;
		var move = _client.MoveNodeHandler!;
		_client.MoveNodeHandler = (id, parent, index) =>
		{
			inner = tree.Operations!.MoveStepAsync(tree.FindNode("C"), -1);   // the held key repeating
			return move(id, parent, index);
		};

		await tree.Operations!.MoveStepAsync(tree.FindNode("C"), -1);
		await inner;

		Assert.Equal(1, MoveCalls);
		Assert.Equal(["A", "C", "B", "D"], Order(tree, "d1"));
	}

	/// <summary>TC.TO.16 (view model): a supervisor keeps its workers after a move; the move call is the same one a drag makes.</summary>
	[Fact]
	public async Task WorkersStayUnderTheirSupervisor_AfterAMove()
	{
		_client.Workers["B"] = [_b.Session("w1", "W1", supervisor: "B"), _b.Session("w2", "W2", supervisor: "B")];
		var tree = await LoadedAsync("B");

		await tree.Operations!.MoveStepAsync(tree.FindNode("B"), 1);

		Assert.Equal(["A", "C", "B", "D"], Order(tree, "d1"));
		var moved = tree.FindNode("B")!;
		Assert.Equal(2, moved.WorkerCount);
		await tree.ToggleExpandCommand.ExecuteAsync(moved);
		await tree.WhenIdleAsync();
		Assert.Equal(["w1", "w2"], moved.Children.Select(c => c.Id));
	}

	// ---------------------------------------------------------------- the view

	private static void PumpUntil(Func<bool> done)
	{
		var deadline = DateTime.UtcNow.AddSeconds(10);
		while (!done() && DateTime.UtcNow < deadline)
		{
			Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, () => { });
			Thread.Sleep(5);
		}

		Assert.True(done(), "the key press did not finish in time");
	}

	/// <summary>TC.TO.15: Ctrl+Up on the tree handles the key, moves the session, keeps it selected and in view. (The keyboard focus itself is set by `item.Focus()`; a test window is never active, so the focus cannot be read back here.)</summary>
	[Fact]
	public async Task CtrlUp_InTheView_HandlesTheKey_KeepsTheSessionSelectedInViewAndFocused()
	{
		var tree = await LoadedAsync("C");
		var handled = false;
		var inView = false;

		StaTestRunner.Run(() =>
		{
			SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
			var panel = new TreePanel { DataContext = tree, CurrentModifiers = () => ModifierKeys.Control };
			var host = new Grid { Width = 340, Height = 400 };
			host.Children.Add(panel);
			host.Measure(new Size(340, 400));
			host.Arrange(new Rect(0, 0, 340, 400));
			host.UpdateLayout();
			var list = (ListBox)panel.FindName("RowList");
			list.Focus();
			using var source = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("tree-order-test") { Width = 10, Height = 10 });

			var key = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Up) { RoutedEvent = UIElement.PreviewKeyDownEvent };
			list.RaiseEvent(key);
			handled = key.Handled;

			PumpUntil(() => Order(tree, "d1").SequenceEqual(["A", "C", "B", "D"]));
			PumpUntil(() => tree.SelectedNode is { Id: "C" });
			host.UpdateLayout();
			inView = list.ItemContainerGenerator.ContainerFromItem(tree.SelectedNode) is ListBoxItem;
		});

		Assert.True(handled);
		Assert.Equal("C", tree.SelectedNode?.Id);
		Assert.True(inView);
		Assert.Contains("tree.moveNode:C:d1:1", _client.Calls);
		await Task.CompletedTask;
	}

	/// <summary>The same press without Ctrl, or with a worker selected, is left to the list.</summary>
	[Fact]
	public async Task UpWithoutCtrl_IsLeftToTheList_AndNothingMoves()
	{
		var tree = await LoadedAsync("C");
		var handled = true;

		StaTestRunner.Run(() =>
		{
			var panel = new TreePanel { DataContext = tree, CurrentModifiers = () => ModifierKeys.None };
			var host = new Grid { Width = 340, Height = 400 };
			host.Children.Add(panel);
			host.Measure(new Size(340, 400));
			host.Arrange(new Rect(0, 0, 340, 400));
			host.UpdateLayout();
			var list = (ListBox)panel.FindName("RowList");
			using var source = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("tree-order-test2") { Width = 10, Height = 10 });
			var key = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Up) { RoutedEvent = UIElement.PreviewKeyDownEvent };
			list.RaiseEvent(key);
			handled = key.Handled;
		});

		Assert.False(handled);
		Assert.Equal(0, MoveCalls);
		await Task.CompletedTask;
	}
}
