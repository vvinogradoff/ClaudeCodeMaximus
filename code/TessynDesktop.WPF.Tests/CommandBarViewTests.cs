using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>The command bar's selectors as real controls bound to the session pane (W1a).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CommandBarViewTests
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

	[Fact]
	public void CommandBar_ShowsTheListsAndWritesThePickBackToTheViewModel()
	{
		var listener = new BindingErrors();
		StaTestRunner.Run(() =>
		{
			PresentationTraceSources.Refresh();
			PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
			PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
			var client = new FakeTessynClient
			{
				Catalog = new CatalogResult(
					[new ModelOptionDto("default", "Default", null), new ModelOptionDto("opus", "Opus 5.5", null)],
					["default", "high"],
					[new ProfileOptionDto("default", "[Sys] Default"), new ProfileOptionDto("work", "Work")]),
			};
			client.Sessions["a"] = FakeSession.WithEntries(2);
			var pane = new SessionPaneViewModel(client, new FakeTranscriptSurface(), new MarkdigEntryRenderer(new UrlPolicy()), new FakeUrlLauncher(), new ImmediateUiDispatcher(),
				new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance), NullLogger<SessionPaneViewModel>.Instance);
			pane.OpenAsync("a").GetAwaiter().GetResult();
			pane.LoadCatalogAsync().GetAwaiter().GetResult();

			var bar = new CommandBar { DataContext = pane };
			bar.Measure(new Size(900, 100));
			bar.Arrange(new Rect(0, 0, 900, 100));
			bar.UpdateLayout();

			var boxes = FindAll<ComboBox>(bar);
			Assert.Equal(3, boxes.Count);
			Assert.Equal([2, 2, 2], boxes.Select(b => b.Items.Count));
			Assert.Equal("default", ((SelectorOptionViewModel)boxes[0].SelectedItem).Id);

			boxes[0].SelectedItem = pane.Models[1];
			boxes[2].SelectedItem = pane.Profiles[1];

			Assert.Equal("opus", pane.SelectedModel!.Id);
			Assert.Equal("work", pane.SelectedProfile!.Id);
		});

		Assert.Empty(listener.Messages);
	}

	[Fact]
	public void TurnLoopModule_ContributesTheCommandBarToItsSlot()
	{
		StaTestRunner.Run(() =>
		{
			var slots = new ShellSlots();
			new TurnLoopShellModule().Register(slots, null!);

			var contribution = Assert.Single(slots.For(Constants.Slots.CommandBar));

			Assert.IsType<CommandBar>(contribution.Create());
		});
	}

	private static List<T> FindAll<T>(DependencyObject root) where T : DependencyObject
	{
		var found = new List<T>();
		for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
		{
			var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
			if (child is T match)
				found.Add(match);

			found.AddRange(FindAll<T>(child));
		}

		return found;
	}
}
