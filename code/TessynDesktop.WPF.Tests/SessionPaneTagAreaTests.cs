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
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// Regression guard for FR.24.16: the mini/full tag area is part of the REAL session pane. The control has its own tests (<see cref="SessionTagAreaViewTests"/>); this class checks the
/// pane actually hosts it and wires it, so that a header rework that swaps it for a plain item list again fails here: the area is in the visual tree, bound to the pane's tag area, one line
/// with "+N" by default, expands on a click on empty space, collapses on a double-click on empty space and starts collapsed on every session switch.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionPaneTagAreaTests
{
	private static (SessionPane Pane, SessionPaneViewModel Model) Create(int tags, double width = 900)
	{
		var client = new FakeTessynClient();
		client.Sessions["a"] = FakeSession.WithEntries(3);
		client.Sessions["b"] = FakeSession.WithEntries(3);
		var model = new SessionPaneViewModel(client, new FakeTranscriptSurface(), new MarkdigEntryRenderer(new UrlPolicy()), new FakeUrlLauncher(),
			new ImmediateUiDispatcher(), new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance), NullLogger<SessionPaneViewModel>.Instance,
			Constants.Limits.MaxCachedTranscripts);
		model.OpenAsync("a", "A").GetAwaiter().GetResult();
		for (var i = 0; i < tags; i++)
			model.Tags.Add(new SessionTagViewModel(new SessionTagDto { Kind = SessionTagKind.GitHubPullRequest, Label = "repo#" + (1000 + i), Url = "https://github.com/o/r/pull/" + i }, new FakeUrlLauncher(), new FakeSystemUrlLauncher()));

		var pane = new SessionPane { DataContext = model };
		Settle(pane, width);
		return (pane, model);
	}

	private static void Settle(SessionPane pane, double width = 900)
	{
		for (var i = 0; i < 4; i++)
		{
			pane.Measure(new Size(width, 700));
			pane.Arrange(new Rect(0, 0, width, 700));
			pane.UpdateLayout();
			pane.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Render, () => { });
		}
	}

	/// <summary>Runs a model call whose continuations touch the live view (as in the app, where they return to the UI thread) and pumps the dispatcher until it completes.</summary>
	private static void Await(SessionPane pane, Func<Task> start) => Await(pane, start());

	private static void Await(SessionPane pane, Task task)
	{
		var frame = new System.Windows.Threading.DispatcherFrame();
		task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
		System.Windows.Threading.Dispatcher.PushFrame(frame);
		task.GetAwaiter().GetResult();
	}

	private static SessionTagArea Area(SessionPane pane) => Descendants<SessionTagArea>(pane).Single();

	private static List<T> Descendants<T>(DependencyObject root) where T : DependencyObject
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

	private static void Press(UIElement target, int clickCount)
	{
		var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent, Source = target };
		typeof(MouseButtonEventArgs).GetProperty(nameof(MouseButtonEventArgs.ClickCount))!.GetSetMethod(nonPublic: true)!.Invoke(args, [clickCount]);
		target.RaiseEvent(args);
	}

	[Fact]
	public void TheSessionPane_HostsTheTagArea_BoundToItsTagAreaViewModel_AndNotAPlainItemList()
	{
		StaTestRunner.Run(() =>
		{
			var (pane, model) = Create(60);

			var area = Area(pane);
			Assert.Same(model.TagArea, area.DataContext);
			Assert.True(area.HasTags);
			Assert.Equal(Visibility.Visible, area.Visibility);
			Assert.True(area.ActualHeight > 0);
			// Every chip of the pane's header lives inside the area: no second, plain list of the tags beside it.
			var chipsOutsideTheArea = Descendants<ItemsControl>(pane).Where(i => ReferenceEquals(i.ItemsSource, model.Tags) && Descendants<ItemsControl>(area).All(a => !ReferenceEquals(a, i)));
			Assert.Empty(chipsOutsideTheArea);
		});
	}

	[Fact]
	public void ByDefault_TheAreaIsOneLine_WithAPlusNHint()
	{
		StaTestRunner.Run(() =>
		{
			var (pane, model) = Create(60);

			var area = Area(pane);
			Assert.False(model.TagArea.IsExpanded);
			Assert.True(area.LineHeight > 0);
			Assert.Equal(area.LineHeight, area.AreaHeight, 1);
			Assert.True(area.HiddenCount > 0, "Chips are hidden on the one line, so the +N hint shows");
			var hint = (FrameworkElement)area.FindName("HiddenHint");
			Assert.Equal(Visibility.Visible, hint.Visibility);
			Assert.Equal("+" + area.HiddenCount, ((TextBlock)area.FindName("HiddenHintText")).Text);
		});
	}

	[Fact]
	public void AClickOnEmptySpaceExpands_AndADoubleClickOnEmptySpaceCollapses()
	{
		StaTestRunner.Run(() =>
		{
			var (pane, model) = Create(60);
			var area = Area(pane);
			var mini = area.AreaHeight;

			Press((UIElement)area.FindName("Root"), 1);
			Settle(pane);
			Assert.True(model.TagArea.IsExpanded);
			Assert.True(area.AreaHeight > mini, $"Expanded: {area.AreaHeight} against {mini}");
			Assert.True(area.AreaHeight <= area.LineHeight * Constants.TagArea.MaxFullLines + 1, "At most ten lines");

			Press((UIElement)area.FindName("Root"), 1);
			Press((UIElement)area.FindName("Root"), 2);
			Settle(pane);
			Assert.False(model.TagArea.IsExpanded);
			Assert.Equal(mini, area.AreaHeight, 1);
		});
	}

	[Fact]
	public void ASessionSwitch_ResetsAnExpandedAreaToOneLine()
	{
		StaTestRunner.Run(() =>
		{
			var (pane, model) = Create(60);
			Press((UIElement)Area(pane).FindName("Root"), 1);
			Settle(pane);
			Assert.True(model.TagArea.IsExpanded);

			SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(System.Windows.Threading.Dispatcher.CurrentDispatcher));
			Await(pane, model.OpenAsync("b", "B"));
			Settle(pane);

			Assert.False(model.TagArea.IsExpanded);
		});
	}
}
