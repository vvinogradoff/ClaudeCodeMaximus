using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>The mini/full mode of the session tag area (FR.24): toggling and its persistence in the client state.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TagAreaViewModelTests
{
	private sealed class MemoryState : IClientStateStore
	{
		public ClientStateModel State { get; } = new();

		public int Updates { get; private set; }

		public Task LoadAsync() => Task.CompletedTask;

		public void Update(Action<ClientStateModel> change)
		{
			Updates++;
			change(State);
		}

		public Task FlushAsync() => Task.CompletedTask;
	}

	private static ObservableCollection<SessionTagViewModel> Tags(int count) =>
		new(Enumerable.Range(0, count).Select(i => new SessionTagViewModel(new SessionTagDto { Label = "tag" + i, Url = "https://example.com/" + i }, new FakeUrlLauncher())));

	[Fact]
	public void It_starts_mini_by_default()
	{
		var model = new TagAreaViewModel(Tags(3), new MemoryState());

		Assert.False(model.IsExpanded);
	}

	[Fact]
	public void The_toggle_command_flips_between_mini_and_full()
	{
		var model = new TagAreaViewModel(Tags(3), new MemoryState());

		model.ToggleCommand.Execute(null);
		Assert.True(model.IsExpanded);
		model.ToggleCommand.Execute(null);
		Assert.False(model.IsExpanded);
	}

	[Fact]
	public void A_mode_change_is_written_to_the_client_state()
	{
		var state = new MemoryState();
		var model = new TagAreaViewModel(Tags(3), state);

		model.IsExpanded = true;
		Assert.True(state.State.TagAreaExpanded);

		model.IsExpanded = false;
		Assert.False(state.State.TagAreaExpanded);
		Assert.Equal(2, state.Updates);
	}

	[Fact]
	public void The_remembered_mode_is_restored_by_a_new_view_model()
	{
		var state = new MemoryState();
		new TagAreaViewModel(Tags(1), state).IsExpanded = true;

		var restored = new TagAreaViewModel(Tags(1), state);

		Assert.True(restored.IsExpanded);
		Assert.Equal(1, state.Updates);
	}

	[Fact]
	public void Without_a_state_store_the_mode_still_toggles()
	{
		var model = new TagAreaViewModel(Tags(1), null);

		model.IsExpanded = true;

		Assert.True(model.IsExpanded);
	}

	[Fact]
	public void The_mode_is_global_not_per_session()
	{
		var state = new MemoryState();
		var first = new TagAreaViewModel(Tags(2), state);
		first.IsExpanded = true;

		// Another session's tags, the same client state: the same mode.
		var second = new TagAreaViewModel(Tags(5), state);

		Assert.True(second.IsExpanded);
	}

	[Fact]
	public void The_session_pane_exposes_its_tags_and_the_stored_mode()
	{
		var state = new MemoryState();
		state.State.TagAreaExpanded = true;
		var pane = new SessionPaneViewModel(new FakeTessynClient(), new FakeTranscriptSurface(), new MarkdigEntryRenderer(new UrlPolicy()), new FakeUrlLauncher(),
			new ImmediateUiDispatcher(), new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance), NullLogger<SessionPaneViewModel>.Instance,
			Constants.Limits.MaxCachedTranscripts, stateStore: state);

		Assert.True(pane.TagArea.IsExpanded);
		Assert.Same(pane.Tags, pane.TagArea.Tags);
		Assert.Same(pane.TagArea, pane.TagArea);
	}

	[Fact]
	public async Task The_state_file_round_trips_the_mode()
	{
		var root = Path.Combine(Path.GetTempPath(), "tessyn-tagarea-" + Guid.NewGuid().ToString("N"));
		try
		{
			var paths = new AppPaths(root);
			var first = new JsonClientStateStore(paths, NullLogger<JsonClientStateStore>.Instance, TimeSpan.Zero);
			await first.LoadAsync();
			new TagAreaViewModel(Tags(1), first).IsExpanded = true;
			await first.FlushAsync();

			var second = new JsonClientStateStore(paths, NullLogger<JsonClientStateStore>.Instance, TimeSpan.Zero);
			await second.LoadAsync();

			Assert.True(new TagAreaViewModel(Tags(1), second).IsExpanded);
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}
}
