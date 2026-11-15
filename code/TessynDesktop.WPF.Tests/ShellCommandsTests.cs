using System.Windows.Input;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;

namespace TessynDesktop.WPF.Tests;

/// <summary>The command registry: menus and key gestures bind by command id (W0).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ShellCommandsTests
{
	private sealed class MemoryState : IClientStateStore
	{
		public ClientStateModel State { get; } = new();

		public Task LoadAsync() => Task.CompletedTask;

		public void Update(Action<ClientStateModel> change) => change(State);

		public Task FlushAsync() => Task.CompletedTask;
	}

	private readonly ClientSettingsStore _settings = new(new MemoryState(), NullLogger<ClientSettingsStore>.Instance);
	private readonly CapturingLogger<ShellCommands> _log = new();

	private ShellCommands Create() => new(_settings, _log);

	private static ShellCommand Command(string id, string title = "T", string? gesture = null, Func<object?, Task>? run = null, Func<object?, bool>? can = null) =>
		new() { Id = id, Title = title, DefaultGesture = gesture, ExecuteAsync = run ?? (_ => Task.CompletedTask), CanExecute = can };

	[Fact]
	public void Register_RejectsADuplicateId()
	{
		var commands = Create();
		commands.Register(Command("tree.rename"));

		Assert.Throws<InvalidOperationException>(() => commands.Register(Command("tree.rename")));
	}

	[Fact]
	public async Task ExecuteAsync_PassesTheParameter()
	{
		var commands = Create();
		object? seen = null;
		commands.Register(Command("tree.rename", run: p =>
		{
			seen = p;
			return Task.CompletedTask;
		}));

		var ran = await commands.ExecuteAsync("tree.rename", "node-1");

		Assert.True(ran);
		Assert.Equal("node-1", seen);
	}

	[Fact]
	public async Task ExecuteAsync_ReturnsFalseForUnknownDisabledAndFailingCommands()
	{
		var commands = Create();
		commands.Register(Command("off", can: _ => false));
		commands.Register(Command("boom", run: _ => throw new InvalidOperationException("nope")));

		Assert.False(await commands.ExecuteAsync("missing", null));
		Assert.False(await commands.ExecuteAsync("off", null));
		Assert.False(await commands.ExecuteAsync("boom", null));
		Assert.Contains(_log.Messages, m => m.Contains("boom", StringComparison.Ordinal));
	}

	[Fact]
	public void BuildMenu_OrdersEntries_SeparatesGroups_AndKeepsDisabledOnes()
	{
		var commands = Create();
		commands.Register(Command("c.delete", "Delete", can: _ => false));
		commands.Register(Command("c.rename", "Rename", "F2"));
		commands.Register(Command("c.add", "Add"));
		commands.AddMenuEntry(Constants.Menus.TreeNode, "c.delete", order: 30, group: "danger");
		commands.AddMenuEntry(Constants.Menus.TreeNode, "c.rename", order: 20, group: "edit");
		commands.AddMenuEntry(Constants.Menus.TreeNode, "c.add", order: 10, group: "edit");
		commands.AddMenuEntry(Constants.Menus.TreeNode, "c.unknown", order: 5);
		commands.AddMenuEntry("other.menu", "c.add");

		var menu = commands.BuildMenu(Constants.Menus.TreeNode, null);

		Assert.Equal(["c.add", "c.rename", "c.delete"], menu.Select(i => i.CommandId));
		Assert.Equal([false, false, true], menu.Select(i => i.BeginsGroup));
		Assert.Equal([true, true, false], menu.Select(i => i.IsEnabled));
		Assert.Equal("F2", menu[1].Gesture);
	}

	[Fact]
	public void BuildMenu_PassesTheParameterToCanExecute()
	{
		var commands = Create();
		commands.Register(Command("c.x", can: p => p is "yes"));
		commands.AddMenuEntry("m", "c.x");

		Assert.True(commands.BuildMenu("m", "yes")[0].IsEnabled);
		Assert.False(commands.BuildMenu("m", "no")[0].IsEnabled);
	}

	[Fact]
	public void BuildMenu_OfAMenuWithoutEntries_IsEmpty()
	{
		Assert.Empty(Create().BuildMenu(Constants.Menus.TreeNode, null));
	}

	[Fact]
	public void EqualOrders_KeepRegistrationOrder()
	{
		var commands = Create();
		commands.Register(Command("a"));
		commands.Register(Command("b"));
		commands.AddMenuEntry("m", "b");
		commands.AddMenuEntry("m", "a");

		Assert.Equal(["b", "a"], commands.BuildMenu("m", null).Select(i => i.CommandId));
	}

	[Fact]
	public void FindByGesture_MatchesKeyAndModifiersExactly()
	{
		var commands = Create();
		commands.Register(Command("tree.rename", gesture: "F2"));
		commands.Register(Command("session.send", gesture: "Ctrl+Enter"));
		commands.Register(Command("tree.newGroup", gesture: "Ctrl+Shift+G"));

		Assert.Equal("tree.rename", commands.FindByGesture(Key.F2, ModifierKeys.None)?.Id);
		Assert.Equal("session.send", commands.FindByGesture(Key.Enter, ModifierKeys.Control)?.Id);
		Assert.Equal("tree.newGroup", commands.FindByGesture(Key.G, ModifierKeys.Control | ModifierKeys.Shift)?.Id);
		Assert.Null(commands.FindByGesture(Key.G, ModifierKeys.Control));
		Assert.Null(commands.FindByGesture(Key.F2, ModifierKeys.Control));
	}

	[Fact]
	public void UserOverride_ReplacesTheDefaultGesture_AndAnEmptyOverrideUnbinds()
	{
		var commands = Create();
		commands.Register(Command("tree.rename", gesture: "F2"));
		commands.Register(Command("tree.delete", gesture: "Delete"));
		_settings.Update<KeyBindingSettings>(k =>
		{
			k.Gestures["tree.rename"] = "Ctrl+R";
			k.Gestures["tree.delete"] = "";
		});

		Assert.Null(commands.FindByGesture(Key.F2, ModifierKeys.None));
		Assert.Equal("tree.rename", commands.FindByGesture(Key.R, ModifierKeys.Control)?.Id);
		Assert.Null(commands.FindByGesture(Key.Delete, ModifierKeys.None));
		Assert.Equal("Ctrl+R", commands.GetGesture("tree.rename"));
		Assert.Null(commands.GetGesture("tree.delete"));
		Assert.Null(commands.GetGesture("not.registered"));
	}

	[Fact]
	public void InvalidGesture_IsIgnoredAndLogged()
	{
		var commands = Create();
		commands.Register(Command("bad", gesture: "Ctrl+Banana"));
		commands.Register(Command("good", gesture: "F5"));

		Assert.Equal("good", commands.FindByGesture(Key.F5, ModifierKeys.None)?.Id);
		Assert.Contains(_log.Messages, m => m.Contains("Ctrl+Banana", StringComparison.Ordinal));
	}

	[Fact]
	public void ShellModuleLoader_RunsEveryModule_AndSkipsAFailingOne()
	{
		var slots = new ShellSlots();
		var commands = Create();
		var loader = new ShellModuleLoader([new FailingModule(), new RegisteringModule()], slots, commands, new CapturingLogger<ShellModuleLoader>());

		loader.Load();

		Assert.NotNull(commands.Find("from.module"));
		Assert.Single(slots.For(Constants.Slots.TreeFooter));
	}

	private sealed class FailingModule : IShellModule
	{
		public void Register(IShellSlots slots, IShellCommands commands) => throw new InvalidOperationException("broken module");
	}

	private sealed class RegisteringModule : IShellModule
	{
		public void Register(IShellSlots slots, IShellCommands commands)
		{
			commands.Register(Command("from.module"));
			slots.Add(new ShellSlotContribution { SlotId = Constants.Slots.TreeFooter, Create = () => throw new NotSupportedException("not created here") });
		}
	}
}
