using System.Text;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>FR.29.5–FR.29.8: the preview window's view model: title, one-way Pin, the group's Auto-close, kind switches, renderers per client and the page address.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class PreviewWindowViewModelTests : IDisposable
{
	private const string GroupId = "text-code";

	private readonly FakeTessynClient _client = new();
	private readonly FakePreviewSettingsService _settings = new(new PreviewGroupModel { Id = GroupId, Name = "Text and code", Patterns = "md txt" }, new PreviewGroupModel { Id = "other", Name = "Other" });
	private readonly FakePreviewContentFactory _content = new();
	private readonly PreviewWindowViewModel _window;

	public PreviewWindowViewModelTests()
	{
		_window = new PreviewWindowViewModel(GroupId, "Text and code", pinned: false, _settings, _content, new ImmediateUiDispatcher());
	}

	public void Dispose()
	{
		_window.Dispose();
		_content.Dispose();
	}

	private static PreviewItemModel File(string name) => new() { Kind = PreviewItemKind.File, FileName = name };

	private static PreviewItemModel Page(string url) => new() { Kind = PreviewItemKind.Url, Url = url };

	private async Task ShowFileAsync(string name, FakeTessynClient? client = null)
	{
		var target = client ?? _client;
		target.Files[name] = (name, "text/markdown", Encoding.UTF8.GetBytes("# " + name));
		_window.Show(File(name), new PreviewTarget(name, name), target);
		await _window.File!.WhenIdleAsync();
	}

	[Fact]
	public async Task Title_OfAFile_IsItsNameAndTheGroup()
	{
		await ShowFileAsync("plan.md");

		Assert.Equal("plan.md - Text and code", _window.Title);
		Assert.NotNull(_window.File);
		Assert.Null(_window.Browser);
		Assert.Equal("plan.md", _window.Item!.FileName);
	}

	[Fact]
	public void Title_OfAPage_FallsBackToTheHostThenFollowsThePageTitle()
	{
		_window.Show(Page("https://github.com/tryhardstop/windows-app/pull/1"), null, null);
		Assert.Equal("github.com - Text and code", _window.Title);

		var surface = _content.BrowserSurfaces.Single();
		surface.Source = "https://github.com/tryhardstop/windows-app/pull/1";
		surface.DocumentTitle = "Fix the thing";
		surface.RaiseState();

		Assert.Equal("Fix the thing - Text and code", _window.Title);
	}

	[Fact]
	public async Task RenamingTheGroup_UpdatesTheTitle()
	{
		await ShowFileAsync("plan.md");

		_window.SetGroupName("Notes");

		Assert.Equal("plan.md - Notes", _window.Title);
	}

	[Fact]
	public void Pin_IsOneWay_RaisesPinnedOnce_AndHidesAutoClose()
	{
		var raised = 0;
		_window.Pinned += (_, _) => raised++;
		Assert.True(_window.CanAutoClose);
		Assert.True(_window.PinCommand.CanExecute(null));

		_window.PinCommand.Execute(null);
		_window.PinCommand.Execute(null);

		Assert.True(_window.IsPinned);
		Assert.Equal(1, raised);
		Assert.False(_window.PinCommand.CanExecute(null));
		Assert.False(_window.CanAutoClose);
		Assert.Equal(Constants.PreviewWindow.PinnedToolTip, _window.PinToolTip);
	}

	[Fact]
	public void APinnedWindowReopenedAfterARestart_StartsPinned()
	{
		using var pinned = new PreviewWindowViewModel(GroupId, "Text and code", pinned: true, _settings, _content, new ImmediateUiDispatcher());

		Assert.True(pinned.IsPinned);
		Assert.False(pinned.CanAutoClose);
		Assert.False(pinned.PinCommand.CanExecute(null));
	}

	[Fact]
	public void AutoCloseButton_FlipsTheGroupsSetting_AndFollowsChangesFromSettings()
	{
		Assert.False(_window.AutoClose);

		_window.ToggleAutoCloseCommand.Execute(null);

		Assert.Equal([(GroupId, true)], _settings.AutoCloseWrites);
		Assert.True(_window.AutoClose);

		var groups = _settings.Groups.ToList();
		groups.Single(g => g.Id == GroupId).AutoClose = false;
		_settings.SaveGroups(groups);   // Settings → Save
		Assert.False(_window.AutoClose);

		_window.ToggleAutoCloseCommand.Execute(null);
		Assert.Equal((GroupId, true), _settings.AutoCloseWrites[^1]);
	}

	[Fact]
	public async Task SwitchingKinds_DisposesTheOldContent()
	{
		await ShowFileAsync("plan.md");
		var fileSurface = _content.FileSurfaces.Single();

		_window.Show(Page("https://example.com/"), null, null);

		Assert.True(fileSurface.Disposed);
		Assert.Null(_window.File);
		Assert.NotNull(_window.Browser);
		var browserSurface = _content.BrowserSurfaces.Single();
		Assert.False(browserSurface.Disposed);

		await ShowFileAsync("again.md");

		Assert.True(browserSurface.Disposed);
		Assert.Null(_window.Browser);
		Assert.Equal(2, _content.FileSurfaces.Count);
		Assert.False(_content.FileSurfaces[1].Disposed);
	}

	[Fact]
	public async Task AFileRenderer_IsKeptWhileTheClientStays_AndReplacedWhenTheClientChanges()
	{
		await ShowFileAsync("one.md");
		var first = _window.File;
		await ShowFileAsync("two.md");
		Assert.Same(first, _window.File);
		Assert.Single(_content.FileSurfaces);

		var other = new FakeTessynClient();
		await ShowFileAsync("three.md", other);

		Assert.NotSame(first, _window.File);
		Assert.Equal(2, _content.FileSurfaces.Count);
		Assert.True(_content.FileSurfaces[0].Disposed);
		Assert.Same(other, _content.Clients[1]);
		Assert.Equal("three.md - Text and code", _window.Title);
	}

	[Fact]
	public void ANewPageInAPageWindow_ReusesItsBrowser()
	{
		_window.Show(Page("https://example.com/a"), null, null);
		_window.Show(Page("https://example.com/b"), null, null);

		Assert.Single(_content.BrowserSurfaces);
		Assert.Equal(["https://example.com/a", "https://example.com/b"], _content.BrowserSurfaces[0].Navigated);
	}

	[Fact]
	public void WhenThePageNavigates_ItemUrlFollows_AndItemChangedIsRaised()
	{
		_window.Show(Page("https://example.com/a"), null, null);
		var raised = 0;
		_window.ItemChanged += (_, _) => raised++;
		var surface = _content.BrowserSurfaces.Single();

		surface.Source = "https://example.com/next?x=1";
		surface.RaiseState();

		Assert.Equal("https://example.com/next?x=1", _window.Item!.Url);
		Assert.Equal(1, raised);

		surface.RaiseState();   // the same address again: nothing to save
		Assert.Equal(1, raised);
	}

	[Fact]
	public async Task Item_ReturnsACopy()
	{
		await ShowFileAsync("plan.md");

		_window.Item!.FileName = "changed.md";

		Assert.Equal("plan.md", _window.Item!.FileName);
	}

	[Fact]
	public async Task AFileWithoutAReadHandle_ShowsAnErrorInsteadOfOpening()
	{
		_window.Show(File("gone.md"), null, _client);
		await _window.File!.WhenIdleAsync();

		Assert.Equal(Constants.PreviewWindow.NoFileHandleMessage, _window.File.Error);
	}

	[Fact]
	public async Task Dispose_ReleasesTheContentAndTheSettingsSubscription()
	{
		await ShowFileAsync("plan.md");
		Assert.Equal(1, _settings.GroupsChangedSubscribers);

		_window.Dispose();

		Assert.True(_content.FileSurfaces.Single().Disposed);
		Assert.Equal(0, _settings.GroupsChangedSubscribers);
	}
}
