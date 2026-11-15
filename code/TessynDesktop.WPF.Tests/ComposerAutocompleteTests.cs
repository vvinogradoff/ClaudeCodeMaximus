using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>W1b autocomplete: when the daemon is asked, how an answer becomes a popup, how a suggestion replaces the trigger, and what happens while the code index is built.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ComposerAutocompleteTests : IDisposable
{
	private const string Node = "a";

	private readonly FakeTessynClient _client = new();
	private readonly List<IDisposable> _disposables = [];

	public void Dispose()
	{
		foreach (var d in _disposables)
			d.Dispose();
	}

	private AutocompleteViewModel CreateViewModel(string? nodeId = Node)
	{
		var vm = new AutocompleteViewModel(_client, new ImmediateUiDispatcher(), NullLogger.Instance, () => nodeId)
		{
			Debounce = TimeSpan.Zero,
			IndexRetry = TimeSpan.FromMilliseconds(10),
		};
		_disposables.Add(vm);
		return vm;
	}

	private static AutocompleteSuggestResult Symbols(int triggerStart, int triggerLength, params string[] labels) => new(
		RemoteProtocol.Composer.AutocompleteModes.Symbol, triggerStart, triggerLength, true,
		[.. labels.Select(l => new AutocompleteItemDto(l, "Ns." + l, "class", "Ns", 0, 2))]);

	private int Asked => _client.Calls.Count(c => c.StartsWith("autocomplete.suggest:", StringComparison.Ordinal));

	private static async Task WaitForAsync(Func<bool> condition, string what)
	{
		var deadline = DateTime.UtcNow.AddSeconds(10);
		while (!condition() && DateTime.UtcNow < deadline)
			await Task.Delay(10);

		Assert.True(condition(), "Not reached in time: " + what);
	}

	// ---- the client-side gate ----

	[Theory]
	[InlineData("", 0, false)]
	[InlineData("plain words", 11, false)]
	[InlineData("see #Fo", 7, true)]
	[InlineData("see ##read", 10, true)]
	[InlineData("see #Foo and more", 17, false)]       // the '#' is in an earlier word
	[InlineData("open C:\\Users\\", 14, true)]
	[InlineData("open C:", 7, false)]
	[InlineData("a/b", 3, false)]
	[InlineData("first line C:\\x\nsecond", 22, false)]  // the path is on an earlier line
	public void MayTrigger_OnlyForTextThatCouldContainATrigger(string text, int caret, bool expected) =>
		Assert.Equal(expected, AutocompleteTrigger.MayTrigger(text, caret));

	[Fact]
	public async Task TextWithoutATrigger_NeverAsksTheDaemon()
	{
		var vm = CreateViewModel();

		vm.Update("just some words", 15);
		await Task.Delay(50);

		Assert.Equal(0, Asked);
		Assert.False(vm.IsOpen);
	}

	// ---- answers ----

	[Fact]
	public async Task ASymbolTrigger_OpensThePopup_WithTheFirstItemSelected()
	{
		_client.AutocompleteAnswer = Symbols(4, 3, "Foo", "FooBar");
		var vm = CreateViewModel();

		vm.Update("see #Fo", 7);

		await WaitForAsync(() => vm.IsOpen, "popup");
		Assert.Equal(["Foo", "FooBar"], vm.Items.Select(i => i.Label));
		Assert.Equal(0, vm.SelectedIndex);
		Assert.Equal(("", "Fo", "o"), (vm.Items[0].Before, vm.Items[0].Match, vm.Items[0].After));
	}

	[Fact]
	public async Task AModeOfNone_ClosesThePopup()
	{
		_client.AutocompleteAnswer = Symbols(0, 2, "Foo");
		var vm = CreateViewModel();
		vm.Update("#Fo", 3);
		await WaitForAsync(() => vm.IsOpen, "popup");

		_client.AutocompleteAnswer = new(RemoteProtocol.Composer.AutocompleteModes.None, 0, 0, true, []);
		vm.Update("#Fo x #", 7);

		await WaitForAsync(() => !vm.IsOpen, "closed");
	}

	[Fact]
	public async Task AnAnswerForTextThatIsNoLongerThere_IsDropped()
	{
		var slow = new TaskCompletionSource<AutocompleteSuggestResult>();
		var calls = 0;
		_client.AutocompleteHandler = (_, _) => ++calls == 1 ? slow.Task : Task.FromResult(Symbols(0, 3, "Newer"));
		var vm = CreateViewModel();

		vm.Update("#Ol", 3);
		await WaitForAsync(() => calls == 1, "first request");
		vm.Update("#New", 4);
		await WaitForAsync(() => vm.IsOpen && vm.Items.Count == 1, "second answer shown");
		slow.SetResult(Symbols(0, 3, "Older"));
		await Task.Delay(50);

		Assert.Equal(["Newer"], vm.Items.Select(i => i.Label));
	}

	[Fact]
	public async Task AFailingDaemon_ClosesThePopup_AndNothingIsThrown()
	{
		_client.AutocompleteHandler = (_, _) => Task.FromException<AutocompleteSuggestResult>(new IOException("gone"));
		var vm = CreateViewModel();

		vm.Update("#Fo", 3);
		await Task.Delay(50);

		Assert.False(vm.IsOpen);
	}

	// ---- the code index ----

	[Fact]
	public async Task WhileTheIndexIsBuilt_ItSaysSo_AsksAgain_AndThenShowsTheItems()
	{
		_client.AutocompleteAnswer = new(RemoteProtocol.Composer.AutocompleteModes.Symbol, 0, 3, IndexReady: false, []);
		var vm = CreateViewModel();

		vm.Update("#Fo", 3);
		await WaitForAsync(() => vm.IsIndexBuilding, "building notice");
		Assert.False(vm.IsOpen);   // not "no results"
		await WaitForAsync(() => Asked >= 3, "asked again");

		_client.AutocompleteAnswer = Symbols(0, 3, "Foo");
		await WaitForAsync(() => vm.IsOpen, "items after the index is ready");
		Assert.False(vm.IsIndexBuilding);
	}

	[Fact]
	public async Task TypingOn_StopsTheWaitForTheIndex()
	{
		_client.AutocompleteAnswer = new(RemoteProtocol.Composer.AutocompleteModes.Symbol, 0, 3, IndexReady: false, []);
		var vm = CreateViewModel();
		vm.Update("#Fo", 3);
		await WaitForAsync(() => vm.IsIndexBuilding, "building notice");

		vm.Update("plain words", 11);   // no trigger any more
		var askedThen = Asked;
		await Task.Delay(100);

		Assert.False(vm.IsIndexBuilding);
		Assert.Equal(askedThen, Asked);
	}

	// ---- accepting ----

	[Fact]
	public async Task Accepting_ReplacesTheTriggerText_AndPutsTheCaretBehindIt()
	{
		_client.AutocompleteAnswer = Symbols(4, 3, "Foo", "FooBar");
		var vm = CreateViewModel();
		vm.Update("see #Fo and more", 7);
		await WaitForAsync(() => vm.IsOpen, "popup");

		var insertion = vm.Accept("see #Fo and more");

		Assert.Equal("see Ns.Foo and more", insertion!.Text);
		Assert.Equal("see Ns.Foo".Length, insertion.Caret);
		Assert.False(vm.IsOpen);
	}

	[Fact]
	public async Task Accepting_AfterMovingDown_UsesTheSelectedRow()
	{
		_client.AutocompleteAnswer = Symbols(0, 3, "Foo", "FooBar", "FooBaz");
		var vm = CreateViewModel();
		vm.Update("#Fo", 3);
		await WaitForAsync(() => vm.IsOpen, "popup");

		vm.Move(1);
		vm.Move(1);

		Assert.Equal("Ns.FooBaz", vm.Accept("#Fo")!.Text);
	}

	[Fact]
	public async Task Move_WrapsAtBothEnds()
	{
		_client.AutocompleteAnswer = Symbols(0, 3, "A1", "A2");
		var vm = CreateViewModel();
		vm.Update("#A1", 3);
		await WaitForAsync(() => vm.IsOpen, "popup");

		vm.Move(-1);
		Assert.Equal(1, vm.SelectedIndex);
		vm.Move(1);
		Assert.Equal(0, vm.SelectedIndex);
	}

	[Fact]
	public async Task Accepting_WhenTheTextChangedSinceTheAnswer_InsertsNothing_AndCloses()
	{
		_client.AutocompleteAnswer = Symbols(0, 3, "Foo");
		var vm = CreateViewModel();
		vm.Update("#Fo", 3);
		await WaitForAsync(() => vm.IsOpen, "popup");

		var insertion = vm.Accept("#Fox");

		Assert.Null(insertion);
		Assert.False(vm.IsOpen);
	}

	[Fact]
	public async Task AcceptingADirectory_ContinuesThePath()
	{
		_client.AutocompleteAnswer = new(RemoteProtocol.Composer.AutocompleteModes.Path, 5, 5, true,
			[new AutocompleteItemDto("Users", "C:\\Users\\", "directory", null, 0, 0)]);
		var vm = CreateViewModel();
		vm.Update("open C:\\U", 9);
		await WaitForAsync(() => vm.IsOpen, "popup");

		var insertion = vm.Accept("open C:\\U");

		Assert.Equal("open C:\\Users\\", insertion!.Text);
		Assert.True(insertion.ContinuesPath);
	}

	// ---- in the pane ----

	private SessionPaneViewModel CreatePane()
	{
		_client.Sessions[Node] = FakeSession.WithEntries(2, "Session A");
		var pane = new SessionPaneViewModel(_client, new FakeTranscriptSurface(), new MarkdigEntryRenderer(new UrlPolicy()), new FakeUrlLauncher(), new ImmediateUiDispatcher(),
			new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance), NullLogger<SessionPaneViewModel>.Instance);
		pane.DraftSync.Delay = TimeSpan.FromMinutes(5);
		pane.Autocomplete.Debounce = TimeSpan.Zero;
		_disposables.Add(pane);
		return pane;
	}

	[Fact]
	public async Task InThePane_AcceptingASuggestion_PutsTheTextInTheInput_AndRequestsTheCaret()
	{
		_client.AutocompleteAnswer = Symbols(0, 3, "Foo");
		var pane = CreatePane();
		await pane.OpenAsync(Node);
		pane.InputText = "#Fo";
		pane.Autocomplete.Update("#Fo", 3);
		await WaitForAsync(() => pane.Autocomplete.IsOpen, "popup");
		var caret = -1;
		pane.InputCaretRequested += (_, c) => caret = c;

		var insertion = pane.AcceptSuggestion();

		Assert.Equal("Ns.Foo", pane.InputText);
		Assert.Equal(("Ns.Foo".Length, "Ns.Foo".Length), (insertion!.Caret, caret));
	}

	[Fact]
	public async Task InThePane_TheSessionsOwnTextChanges_AreNotTyping()
	{
		var pane = CreatePane();
		await pane.OpenAsync(Node);
		var seen = new List<bool>();
		pane.PropertyChanged += (_, e) =>
		{
			if (e.PropertyName == nameof(SessionPaneViewModel.InputText))
				seen.Add(pane.IsProgrammaticInput);
		};

		pane.InputText = "typed";                       // the box
		pane.InputText = string.Empty;
		_client.DraftTexts[Node] = "from elsewhere";
		_client.RaiseNotification(RemoteProtocol.Composer.DraftChangedEvent, new DraftChangedPayload(Node, "r"));
		await WaitForAsync(() => pane.InputText == "from elsewhere", "draft shown");

		Assert.Equal([false, false, true], seen);
	}
}
