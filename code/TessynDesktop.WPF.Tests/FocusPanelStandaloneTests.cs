using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// FR.6.16, FR.23.21 (amended): the Focus panel is a docked panel's content on its own: Send answers at its top, an empty state without a session, and the tab header
/// with the badge. The data context is the pane's <c>FocusViewModel</c>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class FocusPanelStandaloneTests
{
	private static FocusViewModel CreateFocus(FakeTessynClient client) => new(client, new ImmediateUiDispatcher(), NullLogger<FocusViewModel>.Instance);

	private static void Layout(FrameworkElement element)
	{
		element.Measure(new Size(400, 600));
		element.Arrange(new Rect(0, 0, 400, 600));
		element.UpdateLayout();
	}

	private static IEnumerable<TextBlock> Texts(DependencyObject root)
	{
		var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
		for (var i = 0; i < count; i++)
		{
			var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
			if (child is TextBlock { Visibility: Visibility.Visible } text)
				yield return text;

			foreach (var nested in Texts(child))
				yield return nested;
		}
	}

	[Fact]
	public void WithoutASession_ThePanelShowsItsEmptyState_AndSendAnswersIsThereButDisabled()
	{
		StaTestRunner.Run(() =>
		{
			var focus = CreateFocus(new FakeTessynClient());
			var panel = new FocusPanel { DataContext = focus };
			Layout(panel);

			var button = (Button)panel.FindName("SendAnswersButton")!;
			Assert.Equal(Visibility.Visible, button.Visibility);
			Assert.False(button.Command.CanExecute(null));
			Assert.Equal("Send answers", focus.SendAnswersText);
			Assert.Contains(Texts(panel), t => t.Text == Constants.Texts.NoFocusSession);
			Assert.DoesNotContain(Texts(panel), t => t.Text == Constants.Texts.NoOpenItems);
		});
	}

	[Fact]
	public void WithASessionButNothingOpen_ThePanelSaysNothingNeedsAttention()
	{
		StaTestRunner.Run(() =>
		{
			var client = new FakeTessynClient();
			client.Sessions["a"] = FakeSession.WithEntries(2);
			var focus = CreateFocus(client);
			focus.SetSessionAsync("a").GetAwaiter().GetResult();
			focus.WhenIdleAsync().GetAwaiter().GetResult();
			var panel = new FocusPanel { DataContext = focus };
			Layout(panel);

			Assert.Contains(Texts(panel), t => t.Text == Constants.Texts.NoOpenItems);
			Assert.DoesNotContain(Texts(panel), t => t.Text == Constants.Texts.NoFocusSession);
		});
	}

	[Fact]
	public void SendAnswers_WorksStandalone_ByTheButtonAndByCtrlEnterInAReplyBox_UnderTheSameEnablingRule()
	{
		var calls = new List<string>();
		StaTestRunner.Run(() =>
		{
			var client = new FakeTessynClient();
			client.Sessions["a"] = FakeSession.WithEntries(3);
			client.Attention["a"] =
			[
				new AttentionItemDto { Id = "A1", Number = 1, Kind = AttentionKind.Ask, Status = AttentionStatus.Open, Title = "Which database?", CreatedUtc = DateTimeOffset.UnixEpoch },
			];
			var focus = CreateFocus(client);
			focus.SetSessionAsync("a").GetAwaiter().GetResult();
			focus.WhenIdleAsync().GetAwaiter().GetResult();
			var panel = new FocusPanel { DataContext = focus };
			Layout(panel);
			var button = (Button)panel.FindName("SendAnswersButton")!;
			Assert.False(button.Command.CanExecute(null));   // nothing typed

			focus.Items[0].ReplyText = "Postgres";
			Layout(panel);
			Assert.True(button.Command.CanExecute(null));
			Assert.Contains(Texts(panel), t => t.Text == "Send answers (1)");

			var reply = FindReplyBox(panel);
			Assert.True(panel.HandleReplyKey(reply, System.Windows.Input.Key.Enter, System.Windows.Input.ModifierKeys.Control));
			calls.AddRange(client.Calls);
		});

		Assert.Contains(calls, c => c.StartsWith("attention.reply:a:A1=Postgres", StringComparison.Ordinal));
	}

	private static TextBox FindReplyBox(DependencyObject root)
	{
		var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
		for (var i = 0; i < count; i++)
		{
			var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
			if (child is TextBox box)
				return box;

			if (child is DependencyObject nested && FindReplyBox(nested) is { } found)
				return found;
		}

		return null!;
	}

	[Fact]
	public void TheTabHeader_ShowsFocusAndTheBadgeOfTheOpenCount_HiddenAtZero()
	{
		StaTestRunner.Run(() =>
		{
			var client = new FakeTessynClient();
			client.Sessions["a"] = FakeSession.WithEntries(2);
			client.Attention["a"] =
			[
				new AttentionItemDto { Id = "A1", Number = 1, Kind = AttentionKind.Ask, Status = AttentionStatus.Open, Title = "t", CreatedUtc = DateTimeOffset.UnixEpoch },
				new AttentionItemDto { Id = "A2", Number = 2, Kind = AttentionKind.Todo, Status = AttentionStatus.Open, Title = "t", CreatedUtc = DateTimeOffset.UnixEpoch },
			];
			var focus = CreateFocus(client);
			var header = new FocusTabHeader { DataContext = focus };
			Layout(header);
			var badge = (Border)header.FindName("BadgePill")!;
			Assert.Equal(Visibility.Collapsed, badge.Visibility);
			Assert.Contains(Texts(header), t => t.Text == "Focus");

			focus.SetSessionAsync("a").GetAwaiter().GetResult();
			focus.WhenIdleAsync().GetAwaiter().GetResult();
			Layout(header);

			Assert.Equal(Visibility.Visible, badge.Visibility);
			Assert.Equal("ask", badge.Tag);
			Assert.Equal("2", ((TextBlock)badge.Child).Text);
		});
	}
}
