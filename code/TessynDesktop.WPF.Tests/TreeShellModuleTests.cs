using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>The tree's commands, menu, keys and toolbar in the shell (W2): the registry entries, the key policy and the views bound to a tree.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TreeShellModuleTests
{
	private sealed class MemoryState : IClientStateStore
	{
		public ClientStateModel State { get; } = new();

		public Task LoadAsync() => Task.CompletedTask;

		public void Update(Action<ClientStateModel> change) => change(State);

		public Task FlushAsync() => Task.CompletedTask;
	}

	private sealed class BindingErrors : TraceListener
	{
		public List<string> Messages { get; } = [];

		public override void Write(string? message) => Capture(message);

		public override void WriteLine(string? message) => Capture(message);

		private void Capture(string? message)
		{
			if (message != null && message.Contains("BindingExpression", StringComparison.Ordinal))
				lock (Messages)
					Messages.Add(message);
		}
	}

	private readonly TreeBuilder _b = new();
	private readonly FakeTessynClient _client = new();
	private readonly FakeDialogPresenter _presenter = new();
	private readonly TreeContext _context = new();
	private readonly ShellSlots _slots = new();
	private readonly ShellCommands _commands = new(new ClientSettingsStore(new MemoryState(), NullLogger<ClientSettingsStore>.Instance), NullLogger<ShellCommands>.Instance);

	private async Task<TreeViewModel> RegisteredTreeAsync()
	{
		_client.Directories =
		[
			_b.Directory("d1", "Repo", [_b.Group("g1", "Group A", _b.Session("s2", "In group"))], _b.Session("s1", "Top session")),
		];
		var folders = new ScriptedFolderPicker([], NullLogger<ScriptedFolderPicker>.Instance);
		var operations = new TreeOperations(_client, new DialogService(_presenter, new ImmediateUiDispatcher()), folders, NullLogger<TreeOperations>.Instance);
		var tree = new TreeViewModel(_client, new ImmediateUiDispatcher(), NullLogger<TreeViewModel>.Instance, TimeSpan.Zero, operations);
		_context.Current = tree;
		new TreeShellModule(_context).Register(_slots, _commands);
		await tree.LoadAsync();
		tree.SelectNodeById("s2");
		return tree;
	}

	private IReadOnlyList<ShellMenuItemModel> Menu(TreeNodeViewModel? node) => _commands.BuildMenu(Constants.Menus.TreeNode, node);

	[Fact]
	public async Task TheModuleRegistersTheSixCommands_TheMenuAndTheToolbar()
	{
		var tree = await RegisteredTreeAsync();

		foreach (var id in new[] { Constants.TreeCommands.AddDirectory, Constants.TreeCommands.AddGroup, Constants.TreeCommands.Rename, Constants.TreeCommands.Delete, Constants.TreeCommands.MoveTo, Constants.TreeCommands.RefreshGitOrigins })
			Assert.NotNull(_commands.Find(id));

		Assert.Equal("F2", _commands.GetGesture(Constants.TreeCommands.Rename));
		Assert.Equal("Delete", _commands.GetGesture(Constants.TreeCommands.Delete));
		Assert.Single(_slots.For(Constants.Slots.TreeToolbar));
		Assert.Equal(
			[Constants.TreeCommands.AddSession, Constants.TreeCommands.AddGroup, Constants.TreeCommands.AddDirectory, Constants.TreeCommands.Rename, Constants.TreeCommands.MoveTo, Constants.TreeCommands.Delete, Constants.TreeCommands.RefreshGitOrigins],
			Menu(tree.FindNode("s1")).Select(i => i.CommandId));
	}

	[Fact]
	public async Task TheMenuEnablesEntriesByTheNodeUnderTheMouse()
	{
		var tree = await RegisteredTreeAsync();

		bool Enabled(string node, string command) => Menu(tree.FindNode(node)).Single(i => i.CommandId == command).IsEnabled;

		Assert.False(Enabled("d1", Constants.TreeCommands.Rename));   // a directory
		Assert.False(Enabled("d1", Constants.TreeCommands.MoveTo));
		Assert.True(Enabled("d1", Constants.TreeCommands.AddGroup));
		Assert.True(Enabled("g1", Constants.TreeCommands.Rename));
		Assert.False(Enabled("g1", Constants.TreeCommands.MoveTo));
		Assert.True(Enabled("s1", Constants.TreeCommands.MoveTo));
		Assert.True(Enabled("s1", Constants.TreeCommands.Delete));
		Assert.True(Enabled("s1", Constants.TreeCommands.AddDirectory));
		Assert.False(_commands.BuildMenu(Constants.Menus.TreeNode, null).Single(i => i.CommandId == Constants.TreeCommands.Delete).IsEnabled);   // empty space
	}

	[Fact]
	public async Task AMenuCommand_ActsOnTheNodeItWasOpenedOn_NotOnTheSelection()
	{
		var tree = await RegisteredTreeAsync();   // s2 is selected
		_presenter.Answer = d =>
		{
			d.CancelCommand.Execute(null);
			return Task.CompletedTask;
		};
		_client.DeleteNodeHandler = (_, _) => new TessynDesktop.Models.Remote.TreeDeleteResult(true, null);

		Assert.True(await _commands.ExecuteAsync(Constants.TreeCommands.Delete, tree.FindNode("s1")));

		Assert.Equal(["tree.deleteNode:s1:False"], _client.Calls.Where(c => c.StartsWith("tree.deleteNode", StringComparison.Ordinal)));
	}

	[Fact]
	public async Task F2AndDelete_NeverFireWhileTheUserTypesInATextBox()
	{
		await RegisteredTreeAsync();

		Assert.False(_commands.TryExecuteGesture(Key.F2, ModifierKeys.None, textInputFocused: true));
		Assert.False(_commands.TryExecuteGesture(Key.Delete, ModifierKeys.None, textInputFocused: true));

		Assert.Empty(_presenter.Shown);
		Assert.DoesNotContain(_client.Calls, c => c.StartsWith("tree.deleteNode", StringComparison.Ordinal));
	}

	[Fact]
	public async Task F2AndDelete_DoNothingWhileTheTreeHasNoFocus()
	{
		await RegisteredTreeAsync();   // no window: nothing in the tree has the keyboard focus

		Assert.False(_commands.TryExecuteGesture(Key.F2, ModifierKeys.None, textInputFocused: false));
		Assert.False(_commands.TryExecuteGesture(Key.Delete, ModifierKeys.None, textInputFocused: false));
		Assert.Empty(_presenter.Shown);
	}

	[Fact]
	public void TheKeyPolicy_BlocksTheTreeKeysInTextInputs()
	{
		var rename = new ShellCommand { Id = Constants.TreeCommands.Rename, Title = "Rename", DefaultGesture = "F2", ExecuteAsync = _ => Task.CompletedTask };

		Assert.True(ShellKeyPolicy.Blocks(rename, Key.F2, ModifierKeys.None, textInputFocused: true));
		Assert.False(ShellKeyPolicy.Blocks(rename, Key.F2, ModifierKeys.None, textInputFocused: false));
		StaTestRunner.Run(() =>
		{
			Assert.True(ShellKeyPolicy.IsTextInput(new TextBox()));
			Assert.False(ShellKeyPolicy.IsTextInput(new ListBox()));
		});
	}

	[Fact]
	public void TreeFocus_RecognisesElementsInsideTheTreePanelOnly()
	{
		StaTestRunner.Run(() =>
		{
			var panel = new TreePanel();
			var rows = (ListBox)panel.FindName("RowList");

			Assert.True(TreeFocus.IsInTree(rows));
			Assert.True(TreeFocus.IsInTree(panel));
			Assert.False(TreeFocus.IsInTree(new ListBox()));
			Assert.False(TreeFocus.IsInTree(null));
		});
	}

	[Fact]
	public async Task TheTreePanelAndToolbar_BindToATreeWithoutBindingErrors()
	{
		var tree = await RegisteredTreeAsync();
		var listener = new BindingErrors();
		StaTestRunner.Run(() =>
		{
			PresentationTraceSources.Refresh();
			PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
			PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
			try
			{
				var panel = new TreePanel { DataContext = tree };
				var toolbar = new TreeToolbar { DataContext = tree };
				tree.FindNode("d1")!.GitOrigin = "https://github.com/acme/repo.git";
				tree.FindNode("s1")!.DropFeedback = DropFeedback.Refused;
				tree.FindNode("g1")!.DropFeedback = DropFeedback.Allowed;
				foreach (var element in new FrameworkElement[] { panel, toolbar })
				{
					element.Measure(new Size(400, 600));
					element.Arrange(new Rect(0, 0, 400, 600));
					element.UpdateLayout();
				}

				var names = new List<string>();
				void Collect(DependencyObject root)
				{
					for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
					{
						var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
						if (child is Button button)
							names.Add((string)button.GetValue(System.Windows.Automation.AutomationProperties.NameProperty));

						Collect(child);
					}
				}

				Collect(toolbar);
				Assert.Equal(
					[Constants.TreeAutomation.AddDirectory, Constants.TreeAutomation.AddSession, Constants.TreeAutomation.AddGroup, Constants.TreeAutomation.Rename, Constants.TreeAutomation.Delete, Constants.TreeAutomation.MoveTo, Constants.TreeAutomation.RefreshGitOrigins],
					names);
			}
			finally
			{
				PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
			}
		});

		Assert.Empty(listener.Messages);
	}
}
