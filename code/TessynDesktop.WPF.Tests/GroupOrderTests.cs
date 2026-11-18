using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// UC.TO.03 / TC.TO.22, TC.TO.23: Ctrl+Up / Ctrl+Down move a group one place among the groups of its parent (FR.1.32).
/// The fake daemon keeps the groups of each parent in order and answers <c>tree.moveNode</c> the way the real one does (the index counts after the group left).
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection(nameof(SessionDisplayCollection))]
public sealed class GroupOrderTests
{
	private sealed class NoFolders : IFolderPicker
	{
		public Task<string?> PickAsync(string title) => Task.FromResult<string?>(null);
	}

	private readonly TreeBuilder _b = new();
	private readonly FakeTessynClient _client = new();
	private readonly FakeDialogPresenter _presenter = new();

	/// <summary>The groups of each parent in order: directory d1 holds G1, G2, G3; G1 holds the sub-groups S1, S2.</summary>
	private readonly Dictionary<string, List<string>> _groups = new()
	{
		["d1"] = ["G1", "G2", "G3"],
		["G1"] = ["S1", "S2"],
	};

	private TreeGroupDto Group(string id)
	{
		var subGroups = _groups.TryGetValue(id, out var children) ? children.Select(Group).ToArray() : [];
		var sessions = id == "G3" ? new[] { _b.Session("gs", "GS", running: true) } : [];
		return new TreeGroupDto(id, id, false, subGroups, sessions);
	}

	private void Publish()
	{
		_client.Directories =
		[
			_b.Directory("d1", "Repo", _groups["d1"].Select(Group).ToArray(), _b.Session("A"), _b.Session("B")),
		];
		_client.TreeVersion++;
	}

	private async Task<TreeViewModel> LoadedAsync(string select)
	{
		Publish();
		_client.MoveNodeHandler = (id, parent, index) =>
		{
			if (!_groups.TryGetValue(parent, out var list) || !list.Contains(id))
				throw new TessynRpcException(-32000, "A group can only be moved within its own parent");

			list.Remove(id);
			list.Insert(Math.Min(index ?? 0, list.Count), id);
			Publish();
			return new TreeMoveResult(true);
		};
		var operations = new TreeOperations(_client, new DialogService(_presenter, new ImmediateUiDispatcher()), new NoFolders(), NullLogger<TreeOperations>.Instance);
		var tree = new TreeViewModel(_client, new ImmediateUiDispatcher(), NullLogger<TreeViewModel>.Instance, TimeSpan.Zero, operations);
		await tree.LoadAsync();
		tree.SelectNodeById(select);
		return tree;
	}

	private static string[] Order(TreeViewModel tree, string parent) => tree.FindNode(parent)!.Children.Where(c => c.Kind == TreeNodeKind.Group).Select(c => c.Id).ToArray();

	private int MoveCalls => _client.Calls.Count(c => c.StartsWith("tree.moveNode", StringComparison.Ordinal));

	/// <summary>TC.TO.22: up and down call the daemon with the neighbour's index among the groups; the group stays selected; a sub-group moves among its parent group's groups only.</summary>
	[Fact]
	public async Task CtrlUpAndDown_OnAGroup_MoveItOnePlace_WithTheNeighboursIndex()
	{
		var tree = await LoadedAsync("G3");
		var ops = tree.Operations!;

		await ops.MoveStepAsync(tree.FindNode("G3"), -1);
		Assert.Contains("tree.moveNode:G3:d1:1", _client.Calls);
		await ops.MoveStepAsync(tree.FindNode("G3"), -1);
		Assert.Contains("tree.moveNode:G3:d1:0", _client.Calls);
		Assert.Equal(["G3", "G1", "G2"], Order(tree, "d1"));
		Assert.Equal("G3", tree.SelectedNode?.Id);

		await ops.MoveStepAsync(tree.FindNode("G3"), 1);
		await ops.MoveStepAsync(tree.FindNode("G3"), 1);
		Assert.Contains("tree.moveNode:G3:d1:2", _client.Calls);
		Assert.Equal(["G1", "G2", "G3"], Order(tree, "d1"));
		Assert.Equal(4, MoveCalls);

		await tree.ToggleExpandCommand.ExecuteAsync(tree.FindNode("G1"));
		await tree.WhenIdleAsync();
		await ops.MoveStepAsync(tree.FindNode("S2"), -1);
		Assert.Contains("tree.moveNode:S2:G1:0", _client.Calls);
		Assert.Equal(["S2", "S1"], Order(tree, "G1"));
		Assert.Equal(["G1", "G2", "G3"], Order(tree, "d1"));
	}

	/// <summary>TC.TO.22: the first group up, the last down, a directory, and a search in progress make no call.</summary>
	[Fact]
	public async Task FirstGroupUp_LastGroupDown_Directory_AndAnActiveSearch_DoNothing()
	{
		var tree = await LoadedAsync("G1");
		var ops = tree.Operations!;

		await ops.MoveStepAsync(tree.FindNode("G1"), -1);
		await ops.MoveStepAsync(tree.FindNode("G3"), 1);
		await ops.MoveStepAsync(tree.FindNode("d1"), 1);
		await ops.MoveStepAsync(tree.FindNode("d1"), -1);
		Assert.Equal(0, MoveCalls);

		_client.SearchHandler = q => new TreeSearchResult(q, false, false, false, [new TreeSearchMatchDto("A", 1, 0, false, [])]);
		tree.SearchText = "A";
		await tree.WhenIdleAsync();
		Assert.True(tree.IsSearching);
		await ops.MoveStepAsync(tree.FindNode("G2"), -1);
		Assert.Equal(0, MoveCalls);
	}

	/// <summary>TC.TO.22 / FR.1.32: a running session inside the group does not block the move; no refusal text.</summary>
	[Fact]
	public async Task AGroupHoldingARunningSession_IsMoved()
	{
		var tree = await LoadedAsync("G3");

		await tree.Operations!.MoveStepAsync(tree.FindNode("G3"), -1);

		Assert.Equal(1, MoveCalls);
		Assert.Equal(["G1", "G3", "G2"], Order(tree, "d1"));
		Assert.NotEqual(Constants.TreeTexts.SessionRunning, tree.StatusText);
	}

	/// <summary>TC.TO.22: a press while a move is in flight is dropped, not queued.</summary>
	[Fact]
	public async Task APressWhileAGroupMoveIsInFlight_IsIgnored()
	{
		var tree = await LoadedAsync("G3");
		var inner = Task.CompletedTask;
		var move = _client.MoveNodeHandler!;
		_client.MoveNodeHandler = (id, parent, index) =>
		{
			inner = tree.Operations!.MoveStepAsync(tree.FindNode("G3"), -1);
			return move(id, parent, index);
		};

		await tree.Operations!.MoveStepAsync(tree.FindNode("G3"), -1);
		await inner;

		Assert.Equal(1, MoveCalls);
		Assert.Equal(["G1", "G3", "G2"], Order(tree, "d1"));
	}

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

	/// <summary>TC.TO.23: Ctrl+Up on a group in the view handles the key, keeps the group selected and in view, and the groups stay above the sessions.</summary>
	[Fact]
	public async Task CtrlUp_OnAGroupInTheView_HandlesTheKey_KeepsItSelectedInView_AndGroupsStayAboveSessions()
	{
		var tree = await LoadedAsync("G3");
		var handled = false;
		var inView = false;
		string[] rows = [];

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
			using var source = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("group-order-test") { Width = 10, Height = 10 });

			var key = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Up) { RoutedEvent = UIElement.PreviewKeyDownEvent };
			list.RaiseEvent(key);
			handled = key.Handled;

			PumpUntil(() => Order(tree, "d1").SequenceEqual(["G1", "G3", "G2"]));
			PumpUntil(() => tree.SelectedNode is { Id: "G3" });
			host.UpdateLayout();
			inView = list.ItemContainerGenerator.ContainerFromItem(tree.SelectedNode) is ListBoxItem;
			rows = tree.Rows.Select(r => r.Id).ToArray();
		});

		Assert.True(handled);
		Assert.Equal("G3", tree.SelectedNode?.Id);
		Assert.True(inView);
		Assert.Contains("tree.moveNode:G3:d1:1", _client.Calls);
		Assert.Equal(["d1", "G1", "G3", "G2", "A", "B"], rows.Where(r => r is "d1" or "G1" or "G2" or "G3" or "A" or "B").ToArray());
		await Task.CompletedTask;
	}
}
