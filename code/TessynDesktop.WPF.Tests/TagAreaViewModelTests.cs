using System.Collections.ObjectModel;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// The mini/full mode of the session tag area (FR.24): collapsed by default, never remembered; opening another session (also one expanded
/// earlier) collapses it, switching between the Session and Focus views keeps it.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TagAreaViewModelTests
{
	private static ObservableCollection<SessionTagViewModel> Tags(int count) =>
		new(Enumerable.Range(0, count).Select(i => new SessionTagViewModel(new SessionTagDto { Label = "tag" + i, Url = "https://example.com/" + i }, new FakeUrlLauncher(), new FakeSystemUrlLauncher())));

	private static SessionPaneViewModel Pane() =>
		new(new FakeTessynClient(), new FakeTranscriptSurface(), new MarkdigEntryRenderer(new UrlPolicy()), new FakeUrlLauncher(),
			new ImmediateUiDispatcher(), new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance), NullLogger<SessionPaneViewModel>.Instance,
			Constants.Limits.MaxCachedTranscripts);

	[Fact]
	public void It_starts_mini_by_default()
	{
		Assert.False(new TagAreaViewModel(Tags(3)).IsExpanded);
	}

	[Fact]
	public void The_toggle_command_flips_between_mini_and_full()
	{
		var model = new TagAreaViewModel(Tags(3));

		model.ToggleCommand.Execute(null);
		Assert.True(model.IsExpanded);
		model.ToggleCommand.Execute(null);
		Assert.False(model.IsExpanded);
	}

	[Fact]
	public void A_new_view_model_never_restores_an_earlier_mode()
	{
		new TagAreaViewModel(Tags(1)).IsExpanded = true;

		Assert.False(new TagAreaViewModel(Tags(1)).IsExpanded);
	}

	[Fact]
	public void The_session_pane_exposes_its_tags_collapsed()
	{
		var pane = Pane();

		Assert.False(pane.TagArea.IsExpanded);
		Assert.Same(pane.Tags, pane.TagArea.Tags);
		Assert.Same(pane.TagArea, pane.TagArea);
	}

	[Fact]
	public async Task Opening_a_session_starts_collapsed_even_when_the_area_was_expanded_before()
	{
		var pane = Pane();
		pane.TagArea.IsExpanded = true;

		await pane.OpenAsync("a");

		Assert.False(pane.TagArea.IsExpanded);
	}

	[Fact]
	public async Task Switching_sessions_collapses_and_switching_back_to_an_expanded_one_collapses_again()
	{
		var pane = Pane();
		await pane.OpenAsync("a");
		pane.TagArea.IsExpanded = true;

		await pane.OpenAsync("b");
		Assert.False(pane.TagArea.IsExpanded);

		pane.TagArea.IsExpanded = true;
		await pane.OpenAsync("a");
		Assert.False(pane.TagArea.IsExpanded);
	}

	[Fact]
	public async Task Pressing_the_focus_button_keeps_the_state()
	{
		var pane = Pane();
		await pane.OpenAsync("a");
		pane.TagArea.IsExpanded = true;

		pane.BringFocusPanelForwardCommand.Execute(null);
		Assert.True(pane.TagArea.IsExpanded);
	}

	[Fact]
	public async Task Reopening_the_shown_session_keeps_the_state()
	{
		var pane = Pane();
		await pane.OpenAsync("a");
		pane.TagArea.IsExpanded = true;

		await pane.OpenAsync("a");

		Assert.True(pane.TagArea.IsExpanded);
	}

	[Fact]
	public async Task Closing_the_session_collapses_the_area()
	{
		var pane = Pane();
		await pane.OpenAsync("a");
		pane.TagArea.IsExpanded = true;

		pane.Close();

		Assert.False(pane.TagArea.IsExpanded);
	}

	[Fact]
	public async Task An_old_state_file_with_the_remembered_mode_loads_and_is_ignored_and_not_written_back()
	{
		var root = Path.Combine(Path.GetTempPath(), "tessyn-tagarea-" + Guid.NewGuid().ToString("N"));
		try
		{
			var paths = new AppPaths(root);
			Directory.CreateDirectory(Path.GetDirectoryName(paths.ClientStateFilePath)!);
			await File.WriteAllTextAsync(paths.ClientStateFilePath, "{ \"tagAreaExpanded\": true, \"attachAsFiles\": true }");

			var store = new JsonClientStateStore(paths, NullLogger<JsonClientStateStore>.Instance, TimeSpan.Zero);
			await store.LoadAsync();

			Assert.True(store.Read(s => s.AttachAsFiles));   // the rest of the file still loads
			Assert.False(new TagAreaViewModel(Tags(1)).IsExpanded);

			store.Update(s => s.AttachAsFiles = false);
			await store.FlushAsync();
			using var document = JsonDocument.Parse(await File.ReadAllTextAsync(paths.ClientStateFilePath));
			Assert.False(document.RootElement.TryGetProperty("tagAreaExpanded", out _));
		}
		finally
		{
			TestDirectory.Delete(root);
		}
	}
}
