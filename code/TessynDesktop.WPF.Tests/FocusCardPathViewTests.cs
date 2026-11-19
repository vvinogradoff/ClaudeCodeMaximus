using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>TC.FX.23-26: local paths in a Focus card's title and body are marked and open on Ctrl+click or Invoke, never in the user's own text (FR.23.30), in a real layout.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FocusCardPathViewTests
{
	private const double PanelWidth = 640;
	private const string Absolute = @"C:\work\notes\todo.md";
	private const string Relative = "docs/features/preview-windows.md";
	private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

	private static AttentionItemDto Item(int n, AttentionStatus status = AttentionStatus.Open, string? answer = null) => new()
	{
		Id = "A" + n, Number = n, Kind = AttentionKind.Ask, Status = status, Answer = answer,
		Title = $"Read {Absolute} first", Body = $"The plan is in `{Relative}` and a plain word, then [a link](https://example.com/x.md).",
		Options = ["Open it"], CreatedUtc = T0.AddMinutes(n), ClosedUtc = status == AttentionStatus.Open ? null : T0.AddMinutes(n + 1),
	};

	private static (FocusViewModel Focus, FocusPanel Panel) Show(params AttentionItemDto[] items)
	{
		var client = new FakeTessynClient();
		client.Attention["a"] = [.. items];
		var focus = new FocusViewModel(client, new ImmediateUiDispatcher(), NullLogger<FocusViewModel>.Instance);
		focus.SetSessionAsync("a").GetAwaiter().GetResult();
		focus.IsClosedExpanded = true;
		foreach (var closed in focus.ClosedItems)
			closed.ToggleBodyCommand.Execute(null);

		var panel = new FocusPanel { DataContext = focus, Width = PanelWidth };
		panel.Measure(new Size(PanelWidth, 2000));
		panel.Arrange(new Rect(0, 0, PanelWidth, 2000));
		panel.UpdateLayout();
		return (focus, panel);
	}

	private static List<T> Descendants<T>(DependencyObject root) where T : DependencyObject
	{
		var result = new List<T>();
		for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
		{
			var child = VisualTreeHelper.GetChild(root, i);
			if (child is T match)
				result.Add(match);
			result.AddRange(Descendants<T>(child));
		}

		return result;
	}

	private static Point CentreOf(FocusCardText card, string path)
	{
		var bounds = card.PathBounds.Single(b => b.Path == path).Bounds;
		Assert.False(bounds.IsEmpty);
		return new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
	}

	[Fact]
	public void TC_FX_23_PathsInTheTitleAndBody_AreMarked_AndOtherWordsAreNot()
	{
		IReadOnlyList<string> marked = [];
		StaTestRunner.Run(() =>
		{
			var (_, panel) = Show(Item(1));
			var card = Descendants<FocusCardText>(panel).Single();
			marked = card.MarkedPaths;
		});

		Assert.Equal([Absolute, Relative], marked);
	}

	[Fact]
	public void TC_FX_24_TheOutcomeLineOfAClosedCard_IsNeverMarked_ButItsTitleAndBodyAre()
	{
		IReadOnlyList<string> marked = [];
		string text = "";
		StaTestRunner.Run(() =>
		{
			var (_, panel) = Show(Item(2, AttentionStatus.Answered, answer: @"see C:\other\mine.md and notes/mine.md"));
			var card = Descendants<FocusCardText>(panel).Single();
			marked = card.MarkedPaths;
			text = card.PlainText;
		});

		Assert.Contains("mine.md", text);   // the outcome line is there
		Assert.Equal([Absolute, Relative], marked);
		Assert.DoesNotContain(marked, p => p.Contains("mine.md", StringComparison.Ordinal));
	}

	[Fact]
	public void TC_FX_25_CtrlClickOnAPath_OpensIt_APlainClickDoesNot()
	{
		var opened = new List<FocusPathRequest>();
		bool plainTaken = true, ctrlTaken = false, ctrlOffPath = true;
		StaTestRunner.Run(() =>
		{
			var (focus, panel) = Show(Item(1));
			focus.PathActivated += (_, r) => opened.Add(r);
			var card = Descendants<FocusCardText>(panel).Single();
			var point = CentreOf(card, Relative);

			plainTaken = card.TryActivatePathAt(point, ModifierKeys.None);
			ctrlOffPath = card.TryActivatePathAt(new Point(card.ActualWidth - 1, 1), ModifierKeys.Control);
			ctrlTaken = card.TryActivatePathAt(point, ModifierKeys.Control);
		});

		Assert.False(plainTaken);
		Assert.False(ctrlOffPath);
		Assert.True(ctrlTaken);
		var request = Assert.Single(opened);
		Assert.Equal(new FocusPathRequest("a", "A1", Relative, false), request);
	}

	[Fact]
	public void TC_FX_25_CtrlClickOnAPathOfAnExpandedClosedCard_IsMarkedClosed()
	{
		var opened = new List<FocusPathRequest>();
		StaTestRunner.Run(() =>
		{
			var (focus, panel) = Show(Item(3, AttentionStatus.Dismissed));
			focus.PathActivated += (_, r) => opened.Add(r);
			var card = Descendants<FocusCardText>(panel).Single();

			Assert.True(card.TryActivatePathAt(CentreOf(card, Absolute), ModifierKeys.Control));
		});

		var request = Assert.Single(opened);
		Assert.True(request.IsClosed);
		Assert.Equal(Absolute, request.Path);
	}

	[Fact]
	public void TC_FX_26_InvokeOnAPathPeer_RaisesTheRequest_AndTheCardTextKeepsItsName()
	{
		var opened = new List<FocusPathRequest>();
		string name = "";
		List<string> childNames = [];
		StaTestRunner.Run(() =>
		{
			var (focus, panel) = Show(Item(1));
			focus.PathActivated += (_, r) => opened.Add(r);
			var card = Descendants<FocusCardText>(panel).Single();
			var peer = UIElementAutomationPeer.CreatePeerForElement(card);
			name = peer.GetName();
			var children = peer.GetChildren() ?? [];
			childNames = [.. children.Select(c => c.GetName())];
			var path = children.Single(c => c.GetName() == Relative);
			Assert.Equal(AutomationControlType.Hyperlink, path.GetAutomationControlType());
			((IInvokeProvider)path.GetPattern(PatternInterface.Invoke)!).Invoke();
		});

		Assert.StartsWith("A1", name);
		Assert.Contains(Absolute, childNames);
		Assert.Equal(Relative, Assert.Single(opened).Path);
	}

	[Fact]
	public void TC_FX_25_ASelectionDrag_AcrossAPath_StillSelectsTextWithNoRequest()
	{
		var opened = 0;
		string selected = "";
		StaTestRunner.Run(() =>
		{
			var (focus, panel) = Show(Item(1));
			focus.PathActivated += (_, _) => opened++;
			var card = Descendants<FocusCardText>(panel).Single();
			card.Selection.Select(card.Document.ContentStart, card.Document.ContentEnd);
			selected = card.Selection.Text;
		});

		Assert.Equal(0, opened);
		Assert.Contains(Absolute, selected);
		Assert.Contains(Relative, selected);
	}
}
