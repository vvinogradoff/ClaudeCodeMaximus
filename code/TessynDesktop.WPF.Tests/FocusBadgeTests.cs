using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// FR.23.13 / TC.FV.20 to TC.FV.22: the Focus button shows the number of open items other than Decided, hidden at 0 and while Focus is selected, on the kind colour of the most
/// severe counted item (Critical, Ask, Todo, Alert) from the palette. The tree, Recent list and dropdown count Decided items too; the badge does not.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class FocusBadgeTests : IDisposable
{
	private static readonly Color CriticalColour = Color.FromRgb(0x8B, 0, 0);
	private static readonly Color AskColour = Color.FromRgb(0xDA, 0xA5, 0x20);
	private static readonly Color TodoColour = Color.FromRgb(0x10, 0x20, 0x30);

	private readonly FakeTessynClient _client = new();
	private readonly SessionPaneViewModel _pane;

	public FocusBadgeTests()
	{
		var ui = new ImmediateUiDispatcher();
		_pane = new SessionPaneViewModel(
			_client, new FakeTranscriptSurface(), new MarkdigEntryRenderer(new UrlPolicy()), new FakeUrlLauncher(), ui, new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance),
			NullLogger<SessionPaneViewModel>.Instance, Constants.Limits.MaxCachedTranscripts,
			new FocusViewModel(_client, ui, NullLogger<FocusViewModel>.Instance), null, new MediaPreviewPolicy());
	}

	public void Dispose() => _pane.Dispose();

	private static AttentionItemDto Item(int number, AttentionKind kind) =>
		new() { Id = "A" + number, Number = number, Kind = kind, Status = AttentionStatus.Open, Title = "Item " + number, CreatedUtc = DateTimeOffset.UnixEpoch };

	private async Task OpenWith(string id, params AttentionItemDto[] items)
	{
		_client.Sessions[id] = FakeSession.WithEntries(2, "Session " + id);
		_client.Attention[id] = [.. items];
		await _pane.OpenAsync(id);
		await _pane.Focus.WhenIdleAsync();
	}

	[Fact]
	public async Task TheCount_ExcludesDecidedItems_AndTheBadgeIsHiddenAtZero()
	{
		await OpenWith("a", Item(1, AttentionKind.Ask), Item(2, AttentionKind.Decided), Item(3, AttentionKind.Decided));
		Assert.Equal(1, _pane.FocusBadge);
		Assert.True(_pane.IsFocusBadgeVisible);

		await OpenWith("b", Item(1, AttentionKind.Decided), Item(2, AttentionKind.Decided));
		Assert.Equal(0, _pane.FocusBadge);
		Assert.False(_pane.IsFocusBadgeVisible);
		Assert.Equal(string.Empty, _pane.FocusBadgeKind);

		await OpenWith("c");
		Assert.False(_pane.IsFocusBadgeVisible);
	}

	[Fact]
	public async Task TheKind_IsTheMostSevereOpenItem_AndNeverDecided()
	{
		await OpenWith("a", Item(1, AttentionKind.Alert), Item(2, AttentionKind.Todo), Item(3, AttentionKind.Decided));
		Assert.Equal("todo", _pane.FocusBadgeKind);
		Assert.Equal(2, _pane.FocusBadge);

		await OpenWith("b", Item(1, AttentionKind.Alert), Item(2, AttentionKind.Critical), Item(3, AttentionKind.Ask));
		Assert.Equal("critical", _pane.FocusBadgeKind);
		Assert.Equal(3, _pane.FocusBadge);

		await OpenWith("c", Item(1, AttentionKind.Ask), Item(2, AttentionKind.Alert));
		Assert.Equal("ask", _pane.FocusBadgeKind);
	}

	[Fact]
	public async Task TheBadge_ShowsWheneverSomethingIsOpen_AlsoWhileTheFocusPanelIsShown()
	{
		await OpenWith("a", Item(1, AttentionKind.Ask));
		Assert.True(_pane.IsFocusBadgeVisible);
		_pane.BringFocusPanelForwardCommand.Execute(null);
		Assert.True(_pane.IsFocusBadgeVisible);

		await OpenWith("b", Item(1, AttentionKind.Decided));
		Assert.False(_pane.IsFocusBadgeVisible);
	}

	/// <summary>Pumps the dispatcher until the task is done: its continuations touch the live view, as in the app.</summary>
	private static void Pump(Func<Task> start)
	{
		Task task = Task.CompletedTask;
		System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => task = start());   // started inside the dispatcher: the continuations come back to it
		var frame = new System.Windows.Threading.DispatcherFrame();
		task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
		System.Windows.Threading.Dispatcher.PushFrame(frame);
		task.GetAwaiter().GetResult();
	}

	[Fact]
	public void TheRealButton_ShowsTheNumber_OnThePaletteColourOfTheMostSevereKind_AndCollapsesAtZero()
	{
		StaTestRunner.Run(() =>
		{
			var client = new FakeTessynClient();
			var ui = new ImmediateUiDispatcher();
			var model = new SessionPaneViewModel(
				client, new FakeTranscriptSurface(), new MarkdigEntryRenderer(new UrlPolicy()), new FakeUrlLauncher(), ui, new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance),
				NullLogger<SessionPaneViewModel>.Instance, Constants.Limits.MaxCachedTranscripts,
				new FocusViewModel(client, ui, NullLogger<FocusViewModel>.Instance), null, new MediaPreviewPolicy());
			client.Sessions["a"] = FakeSession.WithEntries(2, "A");
			client.Attention["a"] = [Item(1, AttentionKind.Todo), Item(2, AttentionKind.Ask), Item(3, AttentionKind.Decided)];
			client.Sessions["b"] = FakeSession.WithEntries(2, "B");
			client.Attention["b"] = [Item(1, AttentionKind.Decided)];
			model.OpenAsync("a").GetAwaiter().GetResult();
			model.Focus.WhenIdleAsync().GetAwaiter().GetResult();

			var pane = new SessionPane { DataContext = model };
			Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
			pane.Resources["Colour.FocusCritical"] = Frozen(CriticalColour);
			pane.Resources["Colour.FocusAsk"] = Frozen(AskColour);
			pane.Resources["Colour.FocusTodo"] = Frozen(TodoColour);
			void Layout()
			{
				pane.Measure(new Size(900, 700));
				pane.Arrange(new Rect(0, 0, 900, 700));
				pane.UpdateLayout();
			}

			Layout();
			var badge = (Border)pane.FindName("FocusBadgeText")!;
			Assert.Equal(Visibility.Visible, badge.Visibility);
			Assert.Equal("2", ((TextBlock)badge.Child).Text);   // Todo + Ask, not the Decided one
			Assert.Equal(AskColour, ((SolidColorBrush)badge.Background).Color);

			Pump(() => model.OpenAsync("b"));
			Pump(() => model.Focus.WhenIdleAsync());
			Layout();
			Assert.Equal(Visibility.Collapsed, badge.Visibility);   // only a Decided item: count 0
			model.Dispose();
		});
	}
}
