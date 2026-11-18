using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels.Settings;

namespace TessynDesktop.WPF.Tests;

/// <summary>The Settings → Previews tab (FR.29.2, FR.27.8): editing, validation, saving, following the stored groups, and the two actions that ask first.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class PreviewsSectionTests
{
	private sealed class ScriptedDialogs : IDialogService
	{
		public List<ConfirmDialogRequest> Confirms { get; } = [];

		public bool Answer { get; set; } = true;

		public void CancelPending()
		{
		}

		public Task<bool> ConfirmAsync(ConfirmDialogRequest request)
		{
			Confirms.Add(request);
			return Task.FromResult(Answer);
		}

		public Task<string?> InputAsync(InputDialogRequest request) => Task.FromResult<string?>(null);

		public Task<string?> ChoiceAsync(ChoiceDialogRequest request) => Task.FromResult<string?>(null);
	}

	private sealed class FakeCleaner : IBrowserDataCleaner
	{
		public int Calls { get; private set; }

		public bool Result { get; set; } = true;

		public Task<bool> ClearAsync()
		{
			Calls++;
			return Task.FromResult(Result);
		}
	}

	private readonly ClientSettingsStore _store = SettingsTestSupport.NewStore();
	private readonly PreviewSettingsService _service;
	private readonly ScriptedDialogs _dialogs = new();
	private readonly FakeCleaner _cleaner = new();
	private readonly PreviewsSectionViewModel _section;

	public PreviewsSectionTests()
	{
		_service = new PreviewSettingsService(_store);
		_section = new PreviewsSectionViewModel(_service, new PreviewGroupMatcher(), _cleaner, _dialogs, new ImmediateUiDispatcher());
	}

	[Fact]
	public void Loads_TheStoredGroups_InOrder_WithNoChange()
	{
		Assert.Equal("Previews", _section.Title);
		Assert.Equal(["Images", "Text and code", "CSV", "Slack", "Windows app", "Backend and frontend", "Linear", "Web"], _section.Groups.Select(g => g.Name));
		Assert.False(_section.HasChanges);
	}

	[Fact]
	public void EditingAnyField_IsAChange_AndEditingBack_IsNot()
	{
		var row = _section.Groups[0];
		var patterns = row.Patterns;
		row.Name = "Pictures";
		Assert.True(_section.HasChanges);
		row.Name = "Images";
		row.Patterns += " ico";
		Assert.True(_section.HasChanges);
		row.Patterns = patterns;
		row.AutoClose = true;
		Assert.True(_section.HasChanges);
		row.AutoClose = false;
		Assert.False(_section.HasChanges);
	}

	[Fact]
	public void AddGroup_AppendsAnEmptyRow_WithAThirtyTwoDigitHexId()
	{
		_section.AddGroup();

		var row = _section.Groups[^1];
		Assert.Equal(9, _section.Groups.Count);
		Assert.Equal(string.Empty, row.Name);
		Assert.Equal(32, row.Id.Length);
		Assert.True(row.Id.All(char.IsAsciiHexDigit));
		Assert.Same(row, _section.NewGroup);
		Assert.True(_section.HasChanges);
	}

	[Fact]
	public void RemoveAndMove_ChangeTheOrder_AndStopAtTheEnds()
	{
		var first = _section.Groups[0];
		var second = _section.Groups[1];

		_section.MoveUp(first);
		Assert.Same(first, _section.Groups[0]);
		_section.MoveDown(first);
		Assert.Same(first, _section.Groups[1]);
		Assert.Same(second, _section.Groups[0]);
		_section.MoveDown(_section.Groups[^1]);
		Assert.Equal(8, _section.Groups.Count);
		_section.Remove(first);
		Assert.Equal(7, _section.Groups.Count);
		Assert.DoesNotContain(first, _section.Groups);
		Assert.True(_section.HasChanges);
	}

	[Fact]
	public async Task ResetToDefaults_IsAnEditUntilSave()
	{
		_service.SaveGroups([new PreviewGroupModel { Id = "x", Name = "Mine", Patterns = "md" }]);
		Assert.False(_section.HasChanges);

		_section.ResetToDefaults();

		Assert.Equal(8, _section.Groups.Count);
		Assert.True(_section.HasChanges);
		Assert.Single(_service.Groups);
		Assert.True(await _section.SaveAsync());
		Assert.Equal(8, _service.Groups.Count);
		Assert.False(_section.HasChanges);
	}

	[Fact]
	public async Task Save_WritesTheTrimmedGroups_ThroughTheService()
	{
		_section.Groups[0].Name = "  Pictures ";
		_section.Groups[0].Patterns = " png, jpg ";
		_section.Groups[0].AutoClose = true;

		Assert.True(await _section.SaveAsync());

		var stored = _service.Groups[0];
		Assert.Equal(("images", "Pictures", "png, jpg", true), (stored.Id, stored.Name, stored.Patterns, stored.AutoClose));
		Assert.Equal("Saved.", _section.StatusText);
		Assert.False(_section.HasChanges);
	}

	[Fact]
	public async Task Save_RefusesAnEmptyName()
	{
		_section.AddGroup();
		_section.Groups[^1].Patterns = "txt";

		Assert.False(await _section.SaveAsync());

		Assert.Contains("A group has no name.", _section.ErrorText);
		Assert.Equal("A group has no name.", _section.Groups[^1].Error);
		Assert.Equal(8, _service.Groups.Count);
	}

	[Fact]
	public async Task Save_RefusesTwoGroupsWithTheSameName_IgnoringCase()
	{
		_section.Groups[1].Name = "IMAGES";

		Assert.False(await _section.SaveAsync());

		Assert.Contains("The name 'IMAGES' is used by more than one group.", _section.ErrorText);
		Assert.Equal("Images", _service.Groups[0].Name);
	}

	[Fact]
	public async Task Save_RefusesAGroupWithoutPatterns_NamingTheGroup()
	{
		_section.Groups[2].Patterns = " , ";

		Assert.False(await _section.SaveAsync());

		Assert.Contains("Group 'CSV' has no pattern.", _section.ErrorText);
	}

	[Theory]
	[InlineData("md notaurl:/x")]
	[InlineData("md ftp://host/*")]
	[InlineData("md .")]
	[InlineData("md https://")]
	public async Task Save_RefusesAnInvalidPattern_NamingGroupAndPattern(string patterns)
	{
		_section.Groups[2].Patterns = patterns;
		var bad = patterns.Split(' ')[1];

		Assert.False(await _section.SaveAsync());

		Assert.Contains($"Group 'CSV': '{bad}' is not a file extension or an http(s) address.", _section.ErrorText);
		Assert.Contains(bad, _section.Groups[2].Error);
		Assert.Equal("csv", _service.Groups[2].Patterns);
	}

	[Fact]
	public void GroupsChanged_ReloadsTheList_WhenNothingIsEdited()
	{
		_service.SetAutoClose("csv", true);

		Assert.True(_section.Groups.Single(g => g.Id == "csv").AutoClose);
		Assert.False(_section.HasChanges);
	}

	[Fact]
	public void GroupsChanged_KeepsTheEdits_AndMergesAnAutoCloseFlipMadeInAWindow()
	{
		_section.Groups[0].Name = "Pictures";

		_service.SetAutoClose("csv", true);   // a window's Auto-close button

		Assert.Equal("Pictures", _section.Groups[0].Name);
		Assert.True(_section.Groups.Single(g => g.Id == "csv").AutoClose);
		Assert.True(_section.HasChanges);
	}

	[Fact]
	public async Task SaveAfterAWindowFlip_DoesNotWriteTheStaleSwitchBack()
	{
		_section.Groups[0].Name = "Pictures";
		_service.SetAutoClose("csv", true);

		Assert.True(await _section.SaveAsync());

		Assert.True(_service.Groups.Single(g => g.Id == "csv").AutoClose);
		Assert.Equal("Pictures", _service.Groups[0].Name);
		Assert.False(_section.HasChanges);
	}

	[Fact]
	public async Task AnAutoCloseTheUserChangedInTheTab_IsKeptWhenAWindowFlipsAnother()
	{
		_section.Groups.Single(g => g.Id == "csv").AutoClose = true;   // the user's edit, unsaved

		_service.SetAutoClose("images", true);   // a window flips another group

		Assert.True(_section.Groups.Single(g => g.Id == "images").AutoClose);
		Assert.True(_section.Groups.Single(g => g.Id == "csv").AutoClose);
		Assert.True(_section.HasChanges);

		await _section.SaveAsync();
		Assert.True(_service.Groups.Single(g => g.Id == "csv").AutoClose);
		Assert.True(_service.Groups.Single(g => g.Id == "images").AutoClose);
	}

	[Fact]
	public async Task ForgetPositions_AsksFirst_AndCancelDoesNothing()
	{
		_service.SavePlacement("images", new PreviewPlacementModel());
		_dialogs.Answer = false;

		await _section.ForgetPositionsAsync();

		Assert.Single(_dialogs.Confirms);
		Assert.NotNull(_service.GetPlacement("images"));

		_dialogs.Answer = true;
		await _section.ForgetPositionsAsync();

		Assert.Null(_service.GetPlacement("images"));
		Assert.Equal("Window positions forgotten.", _section.StatusText);
	}

	[Fact]
	public async Task ClearBrowserData_AsksFirst_SayingItSignsOutEverywhere_AndCancelDoesNothing()
	{
		_dialogs.Answer = false;

		await _section.ClearBrowserDataAsync();

		Assert.Equal(0, _cleaner.Calls);
		Assert.Contains("signs you out of every site", Assert.Single(_dialogs.Confirms).Message);

		_dialogs.Answer = true;
		await _section.ClearBrowserDataAsync();

		Assert.Equal(1, _cleaner.Calls);
		Assert.Contains("signed out", _section.StatusText);
	}

	[Fact]
	public async Task ClearBrowserData_ShowsAFailureInTheErrorLine()
	{
		_cleaner.Result = false;

		await _section.ClearBrowserDataAsync();

		Assert.False(string.IsNullOrEmpty(_section.ErrorText));
	}

	[Fact]
	public void TheSettingsWindow_ListsPreviews_AmongTheUnsavedTabs()
	{
		var settings = SettingsTestSupport.NewSettings(new FakeTessynClient(), _store, previewSettings: _service);
		Assert.NotNull(settings.Previews);
		Assert.False(settings.HasChanges);

		settings.Previews!.Groups[0].Name = "Pictures";

		Assert.Equal(["Previews"], settings.UnsavedTabs);
		Assert.True(settings.HasChanges);
	}

	[Fact]
	public async Task TheSettingsWindow_SavesThePreviewsWithItsSaveButton()
	{
		var settings = SettingsTestSupport.NewSettings(new FakeTessynClient(), _store, previewSettings: _service);
		settings.Previews!.Groups[0].Name = "Pictures";

		await settings.SaveAsync();

		Assert.Equal("Pictures", _service.Groups[0].Name);
	}
}
