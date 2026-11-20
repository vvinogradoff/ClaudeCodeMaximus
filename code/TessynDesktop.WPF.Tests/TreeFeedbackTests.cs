using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.ViewModels.Settings;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// The Architect's feedback on the left panel and the session header: Avalonia colours (recency tiers, status squares, Focus kinds, selection) editable in Settings,
/// the date format, the resumable square, the profile under the name, the hidden worker groups, the Tags search, rename and delete in the header, and the Switch profile
/// dialog.
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection(nameof(SessionDisplayCollection))]
public sealed class TreeFeedbackTests
{
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

	private sealed class FixedProbe : ISystemThemeProbe
	{
		public bool IsLight { get; set; } = true;

		public event EventHandler? Changed
		{
			add { }
			remove { }
		}
	}

	private static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

	private readonly FakeTessynClient _client = new();
	private readonly TreeBuilder _b = new();
	private readonly ImmediateUiDispatcher _ui = new();

	private static SessionStateDto State(string id, DateTimeOffset? activity = null, DateTimeOffset? prompt = null, bool resumable = false, string? pinned = null, string? profile = null,
		bool draft = false, int attention = 0, AttentionKind kind = AttentionKind.Ask, bool running = false) =>
		new(id, running, false, attention > 0 ? new RowAttentionDto(attention, kind) : null, false, activity, prompt, draft, profile, PinnedProfileName: pinned, Resumable: resumable);

	private async Task<TreeViewModel> LoadAsync(params TreeDirectoryDto[] directories)
	{
		_client.Directories = directories.ToList();
		var tree = new TreeViewModel(_client, _ui, NullLogger<TreeViewModel>.Instance, TimeSpan.Zero);
		await tree.LoadAsync();
		return tree;
	}

	// ---- colours: the catalog follows Avalonia, the applier puts brushes in the resources ----

	[Fact]
	public void TheCatalogDefaults_AreTheAvaloniaColours()
	{
		string Light(string key) => ColourCatalog.Find(key)!.LightDefault;

		Assert.Equal("#008000", Light(ColourCatalog.RecentTier1));   // Green: within 15 minutes
		Assert.Equal("#90EE90", Light(ColourCatalog.RecentTier2));   // LightGreen: within an hour
		Assert.Equal("#F0FFF0", Light(ColourCatalog.RecentTier3));   // HoneyDew: within 4 hours
		Assert.Equal("#F5FFFA", Light(ColourCatalog.RecentTier4));   // MintCream: within 12 hours, or an unsent draft
		Assert.Equal("#006400", Light(ColourCatalog.ActiveBorder));  // DarkGreen dotted border
		Assert.Equal("#8B0000", Light(ColourCatalog.Running));       // DarkRed
		Assert.Equal("#1E90FF", Light(ColourCatalog.Resumable));     // DodgerBlue
		Assert.Equal("#8B0000", Light(ColourCatalog.FocusCritical));
		Assert.Equal("#DAA520", Light(ColourCatalog.FocusAsk));
		Assert.Equal("#707070", Light(ColourCatalog.FocusDecided));
		Assert.Equal(ColourCatalog.All.Count, ColourCatalog.All.Select(d => d.Key).Distinct().Count());
		Assert.All(ColourCatalog.All, d =>
		{
			Assert.True(ColourCatalog.IsValid(d.LightDefault), d.Key);
			Assert.True(ColourCatalog.IsValid(d.DarkDefault), d.Key);
		});
	}

	[Theory]
	[InlineData("#fff", true, "#FFFFFF")]
	[InlineData(" #1e90ff ", true, "#1E90FF")]
	[InlineData("#801E90FF", true, "#801E90FF")]
	[InlineData("red", false, null)]
	[InlineData("#12345", false, null)]
	[InlineData("", false, null)]
	public void AColourEntry_IsValidatedAndNormalized(string text, bool valid, string? normalized)
	{
		Assert.Equal(valid, ColourCatalog.IsValid(text));
		if (valid)
			Assert.Equal(normalized, ColourCatalog.Normalize(text));
	}

	[Fact]
	public void TheColoursTab_HoldsPendingColours_StoresOnApply_OnlyWhatDiffersFromTheDefault_AndResets()
	{
		var store = SettingsTestSupport.NewStore();
		var settings = SettingsTestSupport.NewSettings(new FakeTessynClient(), store);
		var colours = settings.Colours;
		Assert.False(colours.HasChanges);
		var tier1 = colours.Rows.Single(r => r.Key == ColourCatalog.RecentTier1);

		tier1.Set("#ff0000");   // pending (FR.9.19): the window's Save stores it
		Assert.Empty(store.Get<ColourSettings>().Light);
		Assert.True(tier1.IsOverridden);
		Assert.True(tier1.IsPending);
		Assert.True(colours.HasChanges);
		Assert.True(settings.HasChanges);
		settings.SaveCommand.Execute(null);
		Assert.Equal("#FF0000", store.Get<ColourSettings>().Light[ColourCatalog.RecentTier1]);
		Assert.False(colours.HasChanges);

		tier1 = colours.Rows.Single(r => r.Key == ColourCatalog.RecentTier1);
		Assert.False(tier1.IsPending);
		tier1.Set(ColourCatalog.Find(ColourCatalog.RecentTier1)!.LightDefault);   // the default again: pending, and Apply removes the stored colour
		Assert.True(colours.HasChanges);
		colours.ApplyCommand.Execute(null);
		Assert.Empty(store.Get<ColourSettings>().Light);

		tier1 = colours.Rows.Single(r => r.Key == ColourCatalog.RecentTier1);
		tier1.Set("not a colour");   // refused
		Assert.False(tier1.IsOverridden);
		Assert.False(colours.HasChanges);

		tier1.Set("#00FF00");
		colours.ResetAllCommand.Execute(null);
		Assert.All(colours.Rows, r => Assert.False(r.IsOverridden));
		Assert.False(colours.HasChanges);   // back to what is stored
		Assert.Empty(store.Get<ColourSettings>().Light);
	}
	[Fact]
	public void TheApplier_PutsOneBrushPerColourInTheResources_FollowingTheTheme_AndTheUserOverride()
	{
		var store = SettingsTestSupport.NewStore();
		var probe = new FixedProbe();
		var appearance = new AppearanceService(store, probe);
		var applier = new ColourApplier(store, appearance, _ui);

		StaTestRunner.Run(() =>
		{
			var resources = new ResourceDictionary();
			applier.Apply(resources);

			Assert.All(ColourCatalog.All, d => Assert.IsType<SolidColorBrush>(resources[d.ResourceKey]));
			Assert.Equal(Color.FromRgb(0x00, 0x80, 0x00), ((SolidColorBrush)resources["Colour.RecentTier1"]).Color);
			Assert.Equal(Color.FromRgb(0xCD, 0xE6, 0xF7), ((SolidColorBrush)resources["Colour.TreeSelection"]).Color);   // light: pale blue

			store.Update<AppearanceSettings>(a => a.Theme = ClientTheme.Dark);
			store.Update<ColourSettings>(c => c.Dark[ColourCatalog.RecentTier1] = "#FF0000");
			applier.Apply(resources);

			Assert.Equal(Color.FromRgb(0xFF, 0x00, 0x00), ((SolidColorBrush)resources["Colour.RecentTier1"]).Color);
			Assert.Equal(Color.FromRgb(0x26, 0x4F, 0x78), ((SolidColorBrush)resources["Colour.TreeSelection"]).Color);   // dark: the dark default
			Assert.Equal(Color.FromArgb(0x00, 0x00, 0x00, 0x00), ((SolidColorBrush)resources["Colour.FocusAlert"]).Color);   // Alert: transparent in both themes
		});
	}

	// ---- dates and recency ----

	[Fact]
	public void TheDate_IsYyyyMmDd_ByDefault_AndRelativeAgeBySetting()
	{
		var at = new DateTimeOffset(2026, 11, 3, 9, 5, 0, TimeSpan.Zero).ToLocalTime();
		var nowSameDay = at.AddHours(2);
		var nowLater = at.AddDays(5);

		Assert.Equal(at.ToString("yyyy-MM-dd"), SessionDisplay.DateText(at, nowSameDay, SessionDateFormat.Absolute));
		Assert.Equal("2h", SessionDisplay.DateText(at, nowSameDay, SessionDateFormat.Relative));
		Assert.Equal("5d", SessionDisplay.DateText(at, nowLater, SessionDateFormat.Relative));
		Assert.Equal(string.Empty, SessionDisplay.DateText(null, nowLater, SessionDateFormat.Absolute));
	}

	[Fact]
	public void TheAppearanceTab_ChoosesTheDateFormat_AndTheServiceCarriesItToTheRows()
	{
		var store = SettingsTestSupport.NewStore();
		var settings = SettingsTestSupport.NewSettings(new FakeTessynClient(), store);
		var service = new ListDisplayService(store, _ui);
		var raised = 0;
		service.Changed += (_, _) => raised++;
		try
		{
			service.Start();
			Assert.Equal(SessionDateFormat.Absolute, SessionDisplay.DateFormat);

			settings.Appearance.DateFormat = SessionDateFormat.Relative;
			Assert.True(settings.Appearance.HasChanges);
			settings.Appearance.SaveAsync().GetAwaiter().GetResult();

			Assert.Equal(SessionDateFormat.Relative, SessionDisplay.DateFormat);
			Assert.Equal(1, raised);
		}
		finally
		{
			SessionDisplay.DateFormat = SessionDateFormat.Absolute;
		}
	}

	[Fact]
	public void TheRowAndTheNode_ShowTheSameDateAndRecency()
	{
		var prompt = Now.AddMinutes(-40);
		var row = new SessionRowViewModel("s", "N", "d", State("s", prompt: prompt), clock: new FixedClock(Now));
		Assert.Equal(prompt.ToLocalTime().ToString("yyyy-MM-dd"), row.LastPromptText);
		Assert.Equal(3, row.RecencyLevel);

		var node = new TreeNodeViewModel("s", TreeNodeKind.Session, "N", null) { State = State("s", prompt: DateTimeOffset.UtcNow.AddMinutes(-10)) };
		Assert.Equal(4, node.RecencyLevel);
		Assert.True(node.IsRecentlyActive);
		Assert.Equal(prompt.ToLocalTime().Year, DateTimeOffset.UtcNow.ToLocalTime().Year);
		Assert.NotEqual(string.Empty, node.LastPromptText);

		var directory = new TreeNodeViewModel("d", TreeNodeKind.Directory, "Dir", null);
		Assert.Equal((0, false, string.Empty), (directory.RecencyLevel, directory.IsRecentlyActive, directory.LastPromptText));
	}

	[Fact]
	public void ADraftKeepsThePalestTier_AndLastActivityBeatsLastPrompt()
	{
		var node = new TreeNodeViewModel("s", TreeNodeKind.Session, "N", null)
		{
			State = State("s", activity: DateTimeOffset.UtcNow.AddMinutes(-5), prompt: DateTimeOffset.UtcNow.AddHours(-30)),
		};
		Assert.Equal(4, node.RecencyLevel);   // a response five minutes ago, although the last prompt is old

		var draftOnly = new TreeNodeViewModel("t", TreeNodeKind.Session, "N", null) { State = State("t", prompt: DateTimeOffset.UtcNow.AddHours(-30), draft: true) };
		Assert.Equal(1, draftOnly.RecencyLevel);
		Assert.True(draftOnly.HasDraft);
	}

	// ---- the tree row: profile, resumable, attention ----

	[Fact]
	public void TheNode_ShowsTheProfileUnderTheName_TheResumableSquare_AndTheAttentionKind()
	{
		var node = new TreeNodeViewModel("s", TreeNodeKind.Session, "N", null)
		{
			State = State("s", pinned: "Work", profile: "Old", resumable: true, attention: 3, kind: AttentionKind.Critical),
		};

		Assert.Equal("Work", node.ProfileName);
		Assert.True(node.IsResumable);
		Assert.Equal("critical", node.AttentionKey);
		Assert.Equal(3, node.AttentionCount);

		var plain = new TreeNodeViewModel("t", TreeNodeKind.Session, "N", null) { State = State("t") };
		Assert.False(plain.IsResumable);
		Assert.Equal(string.Empty, plain.AttentionKey);
	}

	[Fact]
	public void TheRow_ShowsTheSameMarks()
	{
		var row = new SessionRowViewModel("s", "N", "d", State("s", resumable: true, pinned: "Work", attention: 1, kind: AttentionKind.Todo));

		Assert.True(row.IsResumable);
		Assert.Equal("Work", row.ProfileName);
		Assert.Equal("todo", row.AttentionKey);
		Assert.Equal("N", row.Label);
	}

	[Fact]
	public void TheResumableFlag_ComesFromTheWire()
	{
		var json = """{"id":"s1","running":false,"streamingAlive":false,"favorite":false,"hasDraft":false,"resumable":true}""";
		var state = System.Text.Json.JsonSerializer.Deserialize<SessionStateDto>(json, TessynDesktop.Services.TdcpJsonOptions.Default);

		Assert.True(state!.Resumable);
		Assert.Null(System.Text.Json.JsonSerializer.Deserialize<SessionStateDto>("""{"id":"s1","running":false,"streamingAlive":false,"favorite":false,"hasDraft":false}""", TessynDesktop.Services.TdcpJsonOptions.Default)!.Resumable);
	}

	// ---- worker groups (item 7) ----

	[Fact]
	public async Task AGroupThatHoldsOnlyWorkers_IsEmptyInTheSnapshot_AndHasNoRow_ButWorkersStayNestedAndCollapsed()
	{
		// NicheScan as the daemon reports it: the supervisor on top, and one empty group per orchestration step (the workers are not listed in them).
		_client.Directories =
		[
			_b.Directory("d", "NicheScan",
				[_b.Husk("g1", "scale-research"), _b.Husk("g2", "pilot-run"), _b.Husk("g3", "full-run"), _b.Group("g4", "Notes", _b.Session("n1", "A real note"))],
				_b.Session("s1", "Scan business niches", workers: 747)),
		];
		_client.Workers["s1"] = [_b.Session("w1", "pilot-1", supervisor: "s1")];
		var tree = new TreeViewModel(_client, _ui, NullLogger<TreeViewModel>.Instance, TimeSpan.Zero);
		await tree.LoadAsync();

		Assert.Equal(["d"], tree.Rows.Select(r => r.Id));   // collapsed directory
		await tree.ToggleExpandCommand.ExecuteAsync(tree.Rows[0]);

		Assert.Equal(["d", "g4", "s1"], tree.Rows.Select(r => r.Id).Where(id => id is "d" or "g4" or "s1" or "g1" or "g2" or "g3"));
		Assert.DoesNotContain(tree.Rows, r => r.Id is "g1" or "g2" or "g3");   // no row for a husk
		Assert.DoesNotContain(tree.Rows, r => r.Kind == TreeNodeKind.Worker);   // workers are not loaded, not shown: collapsed under their supervisor
		var supervisor = tree.FindNode("s1")!;
		Assert.True(supervisor.CanExpand);
		Assert.False(supervisor.IsExpanded);
		Assert.Equal(747, supervisor.WorkerCount);
		Assert.True(tree.FindNode("g1")!.IsHusk);   // still a node: an empty group is a place to import into
		Assert.False(tree.FindNode("g4")!.IsHusk);

		await tree.ToggleExpandCommand.ExecuteAsync(supervisor);
		Assert.Contains(tree.Rows, r => r.Id == "w1" && r.Kind == TreeNodeKind.Worker);
	}

	[Fact]
	public async Task ADirectoryWithOnlyHusks_HasNoExpander_AndAnEmptyGroupTheDaemonDoesNotFlagStaysVisible()
	{
		var tree = await LoadAsync(_b.Directory("d", "Only husks", [_b.Husk("g1", "pilot-run")]));
		Assert.False(tree.FindNode("d")!.CanExpand);

		// An empty group of the user's (no HoldsOnlyWorkers): shown, also after a restart, because the daemon tells it from a husk.
		tree = await LoadAsync(_b.Directory("d", "Mine", [_b.Husk("g1", "pilot-run"), _b.Group("g2", "Ideas")]));
		Assert.True(tree.FindNode("d")!.CanExpand);
		Assert.True(tree.FindNode("g1")!.IsHusk);
		Assert.False(tree.FindNode("g2")!.IsHusk);
		await tree.ToggleExpandCommand.ExecuteAsync(tree.Rows[0]);
		Assert.Equal(["d", "g2"], tree.Rows.Select(r => r.Id));
	}

	// ---- Tags ----

	[Theory]
	[InlineData("HSE-*", "HSE-")]
	[InlineData("HSE*240", "HSE 240")]
	[InlineData("  *HSE-?  ", "HSE-")]
	[InlineData("HSE-240", "HSE-240")]
	[InlineData("*", "")]
	public void AWildcardInTheTagSearch_IsAWordSeparator(string typed, string sent) => Assert.Equal(sent, TagSearchViewModel.NormalizeQuery(typed));

	[Fact]
	public async Task TheTagSearch_SendsTheNormalizedText_AndExplainsAnEmptyScopedResult()
	{
		var texts = new List<string>();
		_client.TagsSearchHandler = (text, _) =>
		{
			texts.Add(text);
			return new TagSearchResult(TagEmptyReason.NoMatchingTags, [], TagSessionsEmptyReason.SelectTags, []);
		};
		var tree = await LoadAsync(_b.Directory("d1", "Repo", [], _b.Session("s1", "One")));
		using var tags = new TagSearchViewModel(new NoActions(), _ui, NullLogger<TagSearchViewModel>.Instance, TimeSpan.Zero);
		tags.AttachTree(tree);
		await tags.AttachAsync(_client);
		tree.SelectNodeById("s1");
		await tags.SearchTask;

		tags.SearchText = "HSE-*";
		await tags.SearchTask;

		Assert.Equal("HSE-", texts.Last());
		Assert.True(tags.IsScoped);
		Assert.Equal(Constants.SessionLists.TagsNoMatchScopedText, tags.EmptyChipsText);

		tags.IsScopeEnabled = false;
		await tags.SearchTask;
		Assert.Equal(Constants.SessionLists.TagsNoMatchText, tags.EmptyChipsText);
	}

	[Fact]
	public async Task TheTagSearch_FindsTheHseTagsOfARealisticAnswer()
	{
		// Shaped like the answer of the real daemon for "HSE-": linearIssue chips with counts and a url.
		_client.TagsSearchHandler = (text, _) => new TagSearchResult(null,
			[new TagChipDto("HSE-148", "HSE-148", "https://linear.app/x/issue/HSE-148", SessionTagKind.LinearIssue, 6, false), new TagChipDto("HSE-240", "HSE-240", "https://linear.app/x/issue/HSE-240", SessionTagKind.LinearIssue, 3, false)],
			TagSessionsEmptyReason.SelectTags, []);
		using var tags = new TagSearchViewModel(new NoActions(), _ui, NullLogger<TagSearchViewModel>.Instance, TimeSpan.Zero);
		await tags.AttachAsync(_client);

		tags.SearchText = "HSE-*";
		await tags.SearchTask;

		Assert.Equal(["HSE-148 (6)", "HSE-240 (3)"], tags.Chips.Select(c => c.Text));
		Assert.Null(tags.EmptyChipsText);
	}

	// ---- rename and delete in the session header ----

	private SessionPaneViewModel CreatePane() => new(_client, new FakeTranscriptSurface(), new MarkdigEntryRenderer(new UrlPolicy()), new FakeUrlLauncher(), _ui,
		new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance), NullLogger<SessionPaneViewModel>.Instance);

	[Fact]
	public async Task ADoubleClickRename_EnterAccepts_EscCancels()
	{
		_client.Sessions["a"] = FakeSession.WithEntries(2, "Old name");
		using var pane = CreatePane();
		await pane.OpenAsync("a");

		pane.BeginRenameCommand.Execute(null);
		Assert.True(pane.IsRenaming);
		Assert.Equal("Old name", pane.RenameText);

		pane.RenameText = "Something else";
		pane.CancelRenameCommand.Execute(null);   // Esc
		Assert.False(pane.IsRenaming);
		Assert.Equal("Old name", pane.Title);
		Assert.DoesNotContain(_client.Calls, c => c.StartsWith("tree.renameNode:", StringComparison.Ordinal));

		pane.BeginRenameCommand.Execute(null);
		pane.RenameText = "  New name ";
		await pane.CommitRenameCommand.ExecuteAsync(null);   // Enter

		Assert.False(pane.IsRenaming);
		Assert.Equal("New name", pane.Title);
		Assert.Contains("tree.renameNode:a:New name", _client.Calls);
	}

	[Fact]
	public async Task ARename_ToTheSameOrAnEmptyName_CallsNothing_AndARefusalKeepsTheTitle()
	{
		_client.Sessions["a"] = FakeSession.WithEntries(2, "Old name");
		using var pane = CreatePane();
		await pane.OpenAsync("a");

		pane.BeginRenameCommand.Execute(null);
		await pane.CommitRenameCommand.ExecuteAsync(null);   // unchanged
		pane.BeginRenameCommand.Execute(null);
		pane.RenameText = "   ";
		await pane.CommitRenameCommand.ExecuteAsync(null);   // empty
		Assert.DoesNotContain(_client.Calls, c => c.StartsWith("tree.renameNode:", StringComparison.Ordinal));

		_client.RenameNodeHandler = (_, _) => throw new InvalidOperationException("Name taken");
		pane.BeginRenameCommand.Execute(null);
		pane.RenameText = "Taken";
		await pane.CommitRenameCommand.ExecuteAsync(null);

		Assert.Equal("Old name", pane.Title);
		Assert.Contains("Name taken", pane.Notice);
	}

	[Fact]
	public void ThereIsNothingToRename_WithoutASession()
	{
		using var pane = CreatePane();

		pane.BeginRenameCommand.Execute(null);

		Assert.False(pane.IsRenaming);
	}

	[Fact]
	public async Task TheTitleControl_SwitchesToATextBox_OnTheRenameCommand_AndTheDeleteIconRunsTheTreeDelete()
	{
		_client.Sessions["a"] = FakeSession.WithEntries(2, "Old name");
		_client.Directories = [_b.Directory("d", "Repo", [], _b.Session("a", "Old name"))];
		var tree = new TreeViewModel(_client, _ui, NullLogger<TreeViewModel>.Instance, TimeSpan.Zero);
		await tree.LoadAsync();
		using var pane = CreatePane();
		await pane.OpenAsync("a");
		var deleted = new List<string>();
		var commands = SettingsTestSupport.NewCommands(SettingsTestSupport.NewStore());
		commands.Register(new ShellCommand { Id = Constants.TreeCommands.Delete, Title = "Delete…", ExecuteAsync = node => { deleted.Add(((TreeNodeViewModel)node!).Id); return Task.CompletedTask; } });

		var listener = new BindingErrors();
		StaTestRunner.Run(() =>
		{
			PresentationTraceSources.Refresh();
			PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
			PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
			try
			{
				var title = new SessionTitle { DataContext = pane, Commands = commands, Tree = new TreeContext { Current = tree } };
				Layout(title);
				var text = (TextBlock)title.FindName("TitleText");
				var box = (TextBox)title.FindName("RenameBox");
				var delete = (Button)title.FindName("DeleteButton");
				Assert.Equal(Visibility.Visible, text.Visibility);
				Assert.Equal(Visibility.Collapsed, box.Visibility);
				Assert.Equal(Visibility.Visible, delete.Visibility);
				Assert.True(delete.ActualWidth > 0, "The delete icon is left of the title");
				Assert.True(System.Windows.Automation.AutomationProperties.GetName(delete).Length > 0);

				title.BeginEdit(pane);   // the editing state belongs to the view (FR.6.23.2)
				Layout(title);
				Assert.Equal(Visibility.Collapsed, text.Visibility);
				Assert.Equal(Visibility.Visible, box.Visibility);
				Assert.Equal("Old name", box.Text);

				title.CancelEdit();
				Layout(title);
				Assert.Equal(Visibility.Visible, text.Visibility);

				delete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
			}
			finally
			{
				PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
			}
		});

		Assert.Empty(listener.Messages);
		await Task.Delay(50);
		Assert.Equal(["a"], deleted);
	}

	[Fact]
	public async Task InTheRenameBox_EnterAccepts_EscCancels_AndLeavingTheBoxCancels()
	{
		_client.Sessions["a"] = FakeSession.WithEntries(2, "Old name");
		using var pane = CreatePane();
		await pane.OpenAsync("a");

		StaTestRunner.Run(() =>
		{
			var title = new SessionTitle { DataContext = pane };
			Layout(title);
			var box = (TextBox)title.FindName("RenameBox");
			using var source = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("rename-test") { Width = 10, Height = 10 });

			KeyEventArgs Press(System.Windows.Input.Key key) => new(System.Windows.Input.Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = UIElement.PreviewKeyDownEvent };

			title.BeginEdit(pane);
			box.RaiseEvent(Press(System.Windows.Input.Key.Escape));
			Assert.False(title.IsEditing);

			title.BeginEdit(pane);
			box.Text = "Via Enter";
			box.RaiseEvent(Press(System.Windows.Input.Key.Enter));
			Assert.False(title.IsEditing);
		});

		await Task.Delay(50);
		Assert.Equal("Via Enter", pane.Title);
		Assert.Contains("tree.renameNode:a:Via Enter", _client.Calls);
	}

	// ---- the views ----

	[Fact]
	public async Task TheTreeAndTheRecentList_RenderTheRowFaceWithoutBindingErrors()
	{
		var state = State("s1", activity: DateTimeOffset.UtcNow.AddMinutes(-5), prompt: DateTimeOffset.UtcNow.AddMinutes(-5), resumable: true, pinned: "Work", attention: 2, kind: AttentionKind.Critical, running: true);
		var tree = await LoadAsync(_b.Directory("d1", "Repo", [_b.Husk("g1", "Husk")], _b.SessionWith(state, "Session one"), _b.Session("s2", "Session two", workers: 2)));
		await tree.ToggleExpandCommand.ExecuteAsync(tree.Rows[0]);
		var store = SettingsTestSupport.NewStore();
		var applier = new ColourApplier(store, new AppearanceService(store, new FixedProbe()), _ui);

		var listener = new BindingErrors();
		StaTestRunner.Run(() =>
		{
			PresentationTraceSources.Refresh();
			PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
			PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
			try
			{
				var resources = new ResourceDictionary();
				applier.Apply(resources);
				var panel = new TreePanel { DataContext = tree };
				foreach (System.Collections.DictionaryEntry entry in resources)
					panel.Resources[entry.Key] = entry.Value;
				var host = new Grid { Width = 340, Height = 400 };
				host.Children.Add(panel);
				Layout(host, 340, 400);

				var face = FindAll<SessionRowFace>(panel).First(f => ((TreeNodeViewModel)f.DataContext).Id == "s1");
				var squares = FindAll<System.Windows.Shapes.Rectangle>(face).Where(r => r.Fill is SolidColorBrush && r.Width == 8).ToList();
				Assert.Equal(2, squares.Count(r => r.Visibility == Visibility.Visible));   // running (upper) and resumable (lower)
				var blue = (SolidColorBrush)squares.Single(r => r.Opacity < 1).Fill;
				Assert.Equal(Color.FromRgb(0x1E, 0x90, 0xFF), blue.Color);
				var tint = (Border)face.Content;
				Assert.Equal(Color.FromRgb(0x00, 0x80, 0x00), ((SolidColorBrush)tint.Background).Color);   // activity within 15 minutes: green
				var pill = FindAll<Border>(face).Single(b => "critical".Equals(b.Tag));
				Assert.Equal(Color.FromRgb(0x8B, 0x00, 0x00), ((SolidColorBrush)pill.Background).Color);
				Assert.Contains(FindAll<TextBlock>(face), t => t.Text == "Work");   // the profile under the name

				var recent = new RecentTabViewModel(new NoActions(), _ui, NullLogger<RecentTabViewModel>.Instance, TimeSpan.Zero);
				recent.Attach(tree);
				var list = new RecentPanel { DataContext = recent };
				foreach (System.Collections.DictionaryEntry entry in resources)
					list.Resources[entry.Key] = entry.Value;
				Layout(list, 340, 300);
				Assert.NotEmpty(FindAll<SessionRowFace>(list));
				Assert.NotEmpty(FindAll<TextBlock>(list).Where(t => t.Style?.TargetType == typeof(TextBlock) && t.Text == ""));   // the tree's chevron on the group header
			}
			finally
			{
				PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
			}
		});

		Assert.Empty(listener.Messages);
	}

	[Fact]
	public void TheTreeToolbar_IsOneLineOfFourButtons()
	{
		StaTestRunner.Run(() =>
		{
			var toolbar = new TreeToolbar { DataContext = null };
			Layout(toolbar, 320, 60);

			var buttons = FindAll<Button>(toolbar);
			Assert.Equal(4, buttons.Count);
			Assert.Single(buttons.Select(b => Math.Round(b.TranslatePoint(new Point(0, 0), toolbar).Y)).Distinct());   // all on one line
		});
	}

	[Fact]
	public void TheChoiceDialog_HasFullWidthButtons_WithGenerousSideMargins()
	{
		StaTestRunner.Run(() =>
		{
			var dialog = new DialogViewModel(DialogKind.Choice, "Switch profile", "Pick", string.Empty, "Cancel") { Choices = [new DialogChoice("a", "Work"), new DialogChoice("b", "Private")] };
			var window = new DialogWindow(dialog);
			var content = (FrameworkElement)window.Content;
			Layout(content, 440, 600);

			var buttons = FindAll<Button>(content).Where(b => b.Visibility == Visibility.Visible && b.ActualWidth > 0).ToList();
			Assert.Equal(3, buttons.Count);   // two choices and Cancel
			var inner = 440 - 2 * 44;
			Assert.All(buttons, b => Assert.True(Math.Abs(b.ActualWidth - inner) < 2, $"{b.Content}: {b.ActualWidth} of {inner}"));
			Assert.True(content.Margin.Left >= 40 && content.Margin.Right >= 40, "Generous left and right margins: " + content.Margin);
		});
	}

	[Fact]
	public void TheOtherDialogs_KeepTheirMargins()
	{
		Assert.Equal("20,18,20,16", new DialogViewModel(DialogKind.Confirm, "t", "m", "OK", "Cancel").DialogMargin);
		Assert.Equal("44,24,44,22", new DialogViewModel(DialogKind.Choice, "t", "m", "", "Cancel").DialogMargin);
	}

	[Fact]
	public void TheFocusKindChip_UsesThePalette()
	{
		var applierStore = SettingsTestSupport.NewStore();
		var applier = new ColourApplier(applierStore, new AppearanceService(applierStore, new FixedProbe()), _ui);
		StaTestRunner.Run(() =>
		{
			var resources = new ResourceDictionary();
			applier.Apply(resources);
			var border = new Border { Tag = "ask" };
			border.Resources.MergedDictionaries.Add(resources);
			border.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/TessynDesktop.WPF;component/Styles/KindPill.xaml", UriKind.Relative) });
			border.SetResourceReference(FrameworkElement.StyleProperty, "KindPill");
			Layout(border, 100, 30);

			Assert.Equal(Color.FromRgb(0xDA, 0xA5, 0x20), ((SolidColorBrush)border.Background).Color);
		});
	}

	private sealed class NoActions : ISessionListActions
	{
		public void Open(string nodeId, string name)
		{
		}

		public Task<bool?> SetFavoriteAsync(string nodeId, bool value) => Task.FromResult<bool?>(value);
	}

	private sealed class FixedClock : TimeProvider
	{
		private readonly DateTimeOffset _now;

		public FixedClock(DateTimeOffset now) => _now = now;

		public override DateTimeOffset GetUtcNow() => _now;
	}

	private static void Layout(FrameworkElement element, double width = 400, double height = 300)
	{
		element.Measure(new Size(width, height));
		element.Arrange(new Rect(0, 0, width, height));
		element.UpdateLayout();
	}

	private static List<T> FindAll<T>(DependencyObject root) where T : DependencyObject
	{
		var found = new List<T>();
		void Walk(DependencyObject node)
		{
			for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
			{
				var child = VisualTreeHelper.GetChild(node, i);
				if (child is T match)
					found.Add(match);

				Walk(child);
			}
		}

		Walk(root);
		return found;
	}
}

/// <summary>Tests that change the process-wide date format must not run beside the ones that read it.</summary>
[CollectionDefinition(nameof(SessionDisplayCollection), DisableParallelization = true)]
public sealed class SessionDisplayCollection;
