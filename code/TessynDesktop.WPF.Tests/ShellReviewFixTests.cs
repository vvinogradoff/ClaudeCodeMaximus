using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>Regression tests for the review findings of the W0 shell foundations (S1-S9).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ShellReviewFixTests
{
	private sealed class MemoryState : IClientStateStore
	{
		public ClientStateModel State { get; } = new();

		public Task LoadAsync() => Task.CompletedTask;

		public void Update(Action<ClientStateModel> change) => change(State);

		public Task FlushAsync() => Task.CompletedTask;
	}

	/// <summary>A state store with the real locking, in memory.</summary>
	private sealed class LockingState : IClientStateStore
	{
		private readonly object _gate = new();

		public ClientStateModel State { get; } = new();

		public Task LoadAsync() => Task.CompletedTask;

		public T Read<T>(Func<ClientStateModel, T> read)
		{
			lock (_gate)
				return read(State);
		}

		public void Update(Action<ClientStateModel> change)
		{
			lock (_gate)
				change(State);
		}

		public Task FlushAsync() => Task.CompletedTask;
	}

	private readonly MemoryState _memory = new();
	private readonly CapturingLogger<ShellCommands> _log = new();

	private ShellCommands CreateCommands() => new(new ClientSettingsStore(_memory, NullLogger<ClientSettingsStore>.Instance), _log);

	private static ShellCommand Command(string id, string? gesture = null, Func<object?, Task>? run = null, Func<object?, bool>? can = null, bool allowInText = false, Func<object?>? resolve = null) =>
		new() { Id = id, Title = id, DefaultGesture = gesture, ExecuteAsync = run ?? (_ => Task.CompletedTask), CanExecute = can, AllowInTextInput = allowInText, ResolveParameter = resolve };

	// ---- S1

	[Fact]
	public void SlotHost_ASlotThatThrowsOrReturnsNull_IsSkipped_AndItsNeighboursStillShow()
	{
		StaTestRunner.Run(() =>
		{
			var slots = new ShellSlots();
			slots.Add(new ShellSlotContribution { SlotId = Constants.Slots.CommandBar, Order = 1, Create = () => new TextBlock { Text = "before" } });
			slots.Add(new ShellSlotContribution { SlotId = Constants.Slots.CommandBar, Order = 2, Create = () => throw new InvalidOperationException("no service") });
			slots.Add(new ShellSlotContribution { SlotId = Constants.Slots.CommandBar, Order = 3, Create = () => null! });
			slots.Add(new ShellSlotContribution { SlotId = Constants.Slots.CommandBar, Order = 4, Create = () => new TextBlock { Text = "after" } });
			var log = new CapturingLogger<SlotHost>();
			var host = new SlotHost { SlotId = Constants.Slots.CommandBar, Slots = slots, Logger = log };

			host.Populate();

			Assert.Equal(["before", "after"], host.Items.Cast<TextBlock>().Select(t => t.Text));
			Assert.Equal(2, log.Messages.Count);
		});
	}

	// ---- S2

	private sealed class HalfwayModule : IShellModule
	{
		public void Register(IShellSlots slots, IShellCommands commands)
		{
			slots.Add(new ShellSlotContribution { SlotId = Constants.Slots.TreeFooter, Create = () => new TextBlock() });
			commands.Register(Command("half.one"));
			commands.AddMenuEntry("m", "half.one");
			commands.Register(Command("existing")); // duplicates one registered earlier
		}
	}

	[Fact]
	public void AModuleThatThrowsHalfway_LeavesNoneOfItsRegistrations()
	{
		var slots = new ShellSlots();
		var commands = CreateCommands();
		commands.Register(Command("existing"));
		var log = new CapturingLogger<ShellModuleLoader>();

		new ShellModuleLoader([new HalfwayModule()], slots, commands, log).Load();

		Assert.Empty(slots.For(Constants.Slots.TreeFooter));
		Assert.Null(commands.Find("half.one"));
		Assert.Empty(commands.BuildMenu("m", null));
		Assert.Single(log.Messages);
	}

	[Fact]
	public void EveryRegisteredShellModule_RegistersWithoutFailing()
	{
		var root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-modules-" + Guid.NewGuid().ToString("N"));
		try
		{
			StaTestRunner.Run(() =>
			{
				var services = new ServiceCollection();
				services.AddLogging();
				new ServiceRegistration().Register(services, new AppPaths(root), Dispatcher.CurrentDispatcher, StartupOptions.Parse(["--test-window"]));
				// A module may depend on the connection view model, which is IAsyncDisposable only.
				var provider = services.BuildServiceProvider();
				try
				{
					var log = new CapturingLogger<ShellModuleLoader>();

					var loader = new ShellModuleLoader(provider.GetServices<IShellModule>(), provider.GetRequiredService<IShellSlots>(), provider.GetRequiredService<IShellCommands>(), log);
					loader.Load();

					Assert.Empty(log.Messages);
				}
				finally
				{
					provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
				}
			});
		}
		finally
		{
			TestDirectory.Delete(root);
		}
	}

	// ---- S3, S7, S8

	[Fact]
	public void TextInputFocus_BlocksBareKeysAndEditingShortcuts_UnlessTheCommandOptsIn()
	{
		var plain = Command("a");
		var optedIn = Command("b", allowInText: true);

		Assert.True(ShellKeyPolicy.Blocks(plain, Key.F2, ModifierKeys.None, textInputFocused: true));
		Assert.True(ShellKeyPolicy.Blocks(plain, Key.F2, ModifierKeys.Shift, textInputFocused: true));
		Assert.True(ShellKeyPolicy.Blocks(plain, Key.V, ModifierKeys.Control, textInputFocused: true));
		Assert.True(ShellKeyPolicy.Blocks(plain, Key.Back, ModifierKeys.Control, textInputFocused: true));
		Assert.False(ShellKeyPolicy.Blocks(plain, Key.N, ModifierKeys.Control | ModifierKeys.Shift, textInputFocused: true));
		Assert.False(ShellKeyPolicy.Blocks(plain, Key.F2, ModifierKeys.None, textInputFocused: false));
		Assert.False(ShellKeyPolicy.Blocks(optedIn, Key.F2, ModifierKeys.None, textInputFocused: true));
	}

	[Fact]
	public void IsTextInput_RecognisesTextBoxesPasswordBoxesAndEditableComboBoxesOnly()
	{
		StaTestRunner.Run(() =>
		{
			Assert.True(ShellKeyPolicy.IsTextInput(new TextBox()));
			Assert.True(ShellKeyPolicy.IsTextInput(new PasswordBox()));
			Assert.True(ShellKeyPolicy.IsTextInput(new ComboBox { IsEditable = true }));
			Assert.False(ShellKeyPolicy.IsTextInput(new ComboBox()));
			Assert.False(ShellKeyPolicy.IsTextInput(new ListBox()));
			Assert.False(ShellKeyPolicy.IsTextInput(null));
		});
	}

	[Fact]
	public async Task TryExecuteGesture_InATextInput_LeavesTheKeyAlone_ButRunsAnOptedInCommand()
	{
		var commands = CreateCommands();
		var ran = new List<string>();
		commands.Register(Command("tree.rename", "F2", run: _ =>
		{
			lock (ran)
				ran.Add("rename");
			return Task.CompletedTask;
		}));
		commands.Register(Command("tree.find", "F3", allowInText: true, run: _ =>
		{
			lock (ran)
				ran.Add("find");
			return Task.CompletedTask;
		}));

		Assert.False(commands.TryExecuteGesture(Key.F2, ModifierKeys.None, textInputFocused: true));
		Assert.True(commands.TryExecuteGesture(Key.F3, ModifierKeys.None, textInputFocused: true));
		Assert.True(commands.TryExecuteGesture(Key.F2, ModifierKeys.None, textInputFocused: false));
		await Task.Delay(50);

		lock (ran)
			Assert.Equal(["find", "rename"], ran.Order());
	}

	[Fact]
	public async Task TryExecuteGesture_PassesTheResolvedParameter_AndDeclinesWhenItDoesNotApply()
	{
		var commands = CreateCommands();
		object? seen = null;
		commands.Register(Command("tree.rename", "F2", can: p => p is string, resolve: () => "selected node", run: p =>
		{
			seen = p;
			return Task.CompletedTask;
		}));
		commands.Register(Command("tree.delete", "F4", can: p => p is string));

		Assert.True(commands.TryExecuteGesture(Key.F2, ModifierKeys.None, false));
		await Task.Delay(50);
		Assert.Equal("selected node", seen);

		// no resolver: the key passes null, CanExecute says no, the key is not taken
		Assert.False(commands.TryExecuteGesture(Key.F4, ModifierKeys.None, false));
		Assert.False(commands.TryExecuteGesture(Key.F9, ModifierKeys.None, false));
	}

	[Fact]
	public void ConflictingGestures_UserOverrideWins_ElseFirstRegistered_AndTheConflictIsReported()
	{
		var settings = new ClientSettingsStore(_memory, NullLogger<ClientSettingsStore>.Instance);
		var commands = new ShellCommands(settings, _log);
		commands.Register(Command("first", "F2"));
		commands.Register(Command("second", "F2"));

		Assert.Equal("first", commands.FindByGesture(Key.F2, ModifierKeys.None)?.Id);
		var conflict = Assert.Single(commands.GetConflicts());
		Assert.Equal("first", conflict.Winner);
		Assert.Equal(["second"], conflict.Losers);
		Assert.Contains(_log.Messages, m => m.Contains("first", StringComparison.Ordinal) && m.Contains("second", StringComparison.Ordinal));

		settings.Update<KeyBindingSettings>(k => k.Gestures["second"] = "F2");
		Assert.Equal("second", commands.FindByGesture(Key.F2, ModifierKeys.None)?.Id);
	}

	[Fact]
	public void GestureTable_FollowsLiveKeyBindingChanges_AndNewRegistrations()
	{
		var settings = new ClientSettingsStore(_memory, NullLogger<ClientSettingsStore>.Instance);
		var commands = new ShellCommands(settings, _log);
		commands.Register(Command("tree.rename", "F2"));
		Assert.Equal("tree.rename", commands.FindByGesture(Key.F2, ModifierKeys.None)?.Id);

		settings.Update<KeyBindingSettings>(k => k.Gestures["tree.rename"] = "F6");
		Assert.Null(commands.FindByGesture(Key.F2, ModifierKeys.None));
		Assert.Equal("tree.rename", commands.FindByGesture(Key.F6, ModifierKeys.None)?.Id);

		commands.Register(Command("late", "F7"));
		Assert.Equal("late", commands.FindByGesture(Key.F7, ModifierKeys.None)?.Id);
	}

	// ---- S4, S5

	[ClientSettingsSection("counterSection", version: 1)]
	public sealed class CounterSection
	{
		public int Count { get; set; }
	}

	[Fact]
	public void ConcurrentUpdatesOfOneSection_LoseNothing_WhileReadsRun()
	{
		var settings = new ClientSettingsStore(new LockingState(), NullLogger<ClientSettingsStore>.Instance);
		const int threads = 8;
		const int perThread = 300;

		Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads }, worker =>
		{
			for (var i = 0; i < perThread; i++)
			{
				settings.Update<CounterSection>(c => c.Count++);
				_ = settings.Get<CounterSection>();
			}
		});

		Assert.Equal(threads * perThread, settings.Get<CounterSection>().Count);
	}

	[Fact]
	public void SectionWithMissingDataOrNull_ReadsAsDefaults_InsteadOfThrowing()
	{
		var state = new MemoryState();
		var settings = new ClientSettingsStore(state, NullLogger<ClientSettingsStore>.Instance);
		state.State.Sections["counterSection"] = new ClientSectionModel { Version = 1 }; // Data left undefined
		Assert.Equal(0, settings.Get<CounterSection>().Count);

		state.State.Sections["counterSection"] = null!;
		Assert.Equal(0, settings.Get<CounterSection>().Count);

		state.State.Sections["counterSection"] = new ClientSectionModel { Version = 1, Data = System.Text.Json.JsonSerializer.SerializeToElement("not an object") };
		Assert.Equal(0, settings.Get<CounterSection>().Count);

		settings.Update<CounterSection>(c => c.Count = 5);
		Assert.Equal(5, settings.Get<CounterSection>().Count);
	}

	// ---- S9

	[Fact]
	public async Task ScriptedPresenter_AnswersFromTheScript_AndCancelsWhenItRunsOut()
	{
		var presenter = new ScriptedDialogPresenter(["accept", "text:new name", "choice:b", "bogus"], NullLogger<ScriptedDialogPresenter>.Instance);
		var service = new DialogService(presenter, new ImmediateUiDispatcher());

		Assert.True(await service.ConfirmAsync(new ConfirmDialogRequest("Delete", "Sure?")));
		Assert.Equal("new name", await service.InputAsync(new InputDialogRequest("Rename", "Name", "old")));
		Assert.Equal("b", await service.ChoiceAsync(new ChoiceDialogRequest("Pick", "Which?", [new DialogChoice("a", "A"), new DialogChoice("b", "B")])));
		Assert.False(await service.ConfirmAsync(new ConfirmDialogRequest("Delete", "Sure?"))); // unusable entry
		Assert.False(await service.ConfirmAsync(new ConfirmDialogRequest("Delete", "Sure?"))); // script empty
	}

	[Fact]
	public async Task ScriptedPresenter_AnInputThatFailsValidation_IsCancelledInsteadOfHanging()
	{
		var presenter = new ScriptedDialogPresenter(["text:bad"], NullLogger<ScriptedDialogPresenter>.Instance);
		var service = new DialogService(presenter, new ImmediateUiDispatcher());

		var answer = await service.InputAsync(new InputDialogRequest("Rename", "Name", "old") { Validate = _ => "not allowed" }).WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Null(answer);
	}

	[Fact]
	public void TestWindowOptions_CarryTheDialogScript_OnlyInTestWindowMode()
	{
		Assert.Equal(["accept", "cancel"], StartupOptions.Parse(["--test-window", "--test-dialog", "accept", "--test-dialog", "cancel"]).TestDialogAnswers);
		Assert.Empty(StartupOptions.Parse(["--test-dialog", "accept", "--x", "y"]).TestDialogAnswers);
	}

	/// <summary>A presenter whose dialog is never answered.</summary>
	private sealed class NeverAnsweringPresenter : IDialogPresenter
	{
		public Task<DialogOutcome> PresentAsync(DialogViewModel dialog) => dialog.Outcome;
	}

	/// <summary>A dispatcher that never runs its work (shutting down).</summary>
	private sealed class DeadDispatcher : IUiDispatcher
	{
		public bool CheckAccess() => false;

		public void Post(Action action)
		{
		}
	}

	[Fact]
	public async Task CancelPending_AnswersTheOpenDialogAndTheWaitingOnes_WithCancel()
	{
		var service = new DialogService(new NeverAnsweringPresenter(), new ImmediateUiDispatcher());
		var open = service.ConfirmAsync(new ConfirmDialogRequest("One", "?"));
		var waiting = service.ConfirmAsync(new ConfirmDialogRequest("Two", "?"));

		service.CancelPending();

		Assert.False(await open.WaitAsync(TimeSpan.FromSeconds(5)));
		Assert.False(await waiting.WaitAsync(TimeSpan.FromSeconds(5)));
		Assert.False(await service.ConfirmAsync(new ConfirmDialogRequest("Later", "?")).WaitAsync(TimeSpan.FromSeconds(5)));
	}

	[Fact]
	public async Task CancelPending_FreesACallerWhoseDialogNeverReachedTheUiThread()
	{
		var service = new DialogService(new NeverAnsweringPresenter(), new DeadDispatcher());
		var pending = service.ConfirmAsync(new ConfirmDialogRequest("Lost", "?"));
		await Task.Delay(50);
		Assert.False(pending.IsCompleted);

		service.CancelPending();

		Assert.False(await pending.WaitAsync(TimeSpan.FromSeconds(5)));
	}
}
