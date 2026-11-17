using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.ViewModels.Settings;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// The one Line spacing setting (FR.9.12, TC.LS.01): stored in the appearance section, validated in the Settings tab, sent to the transcript page as
/// <c>lh</c> and applied to the Focus card text through <see cref="LineSpacing.FactorProperty"/>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class LineSpacingTests
{
	private readonly MemoryClientState _state = new();
	private readonly ClientSettingsStore _store;

	public LineSpacingTests() => _store = new ClientSettingsStore(_state, NullLogger<ClientSettingsStore>.Instance);

	private sealed class FakeProbe : ISystemThemeProbe
	{
		public bool IsLight => true;

		public event EventHandler? Changed
		{
			add { }
			remove { }
		}
	}

	// TC.LS.01
	[Fact]
	public void TheDefaultIsTighterThanTheOldTranscriptSpacing_AndWithinTheRange()
	{
		Assert.Equal(1.25, new AppearanceSettings().LineSpacing);
		Assert.True(Constants.Defaults.LineSpacing < 1.45);
		Assert.InRange(Constants.Defaults.LineSpacing, Constants.Settings.MinLineSpacing, Constants.Settings.MaxLineSpacing);
	}

	// TC.LS.02
	[Fact]
	public async Task TheSettingsTab_StoresTheSpacing_AndShowsTheStoredValue()
	{
		var tab = new AppearanceSectionViewModel(_store);
		Assert.Equal("1.25", tab.LineSpacing);

		tab.LineSpacing = "1.5";
		Assert.True(tab.HasChanges);
		Assert.True(await tab.SaveAsync());

		Assert.Equal(1.5, _store.Get<AppearanceSettings>().LineSpacing);
		Assert.Equal(13, _store.Get<AppearanceSettings>().UserFontSize);   // the rest is untouched
		Assert.False(tab.HasChanges);
		Assert.Equal("1.5", new AppearanceSectionViewModel(_store).LineSpacing);
	}

	// TC.LS.03
	[Theory]
	[InlineData("0.9")]
	[InlineData("1.81")]
	[InlineData("tight")]
	public async Task TheSettingsTab_RefusesASpacingOutsideTheRange(string text)
	{
		var tab = new AppearanceSectionViewModel(_store);
		tab.LineSpacing = text;

		Assert.False(await tab.SaveAsync());

		Assert.Equal(1.25, _store.Get<AppearanceSettings>().LineSpacing);
		Assert.NotNull(tab.ErrorText);
	}

	// TC.LS.04
	[Fact]
	public void ChangingTheSpacing_RaisesChanged_AndReachesThePageMessage()
	{
		var service = new AppearanceService(_store, new FakeProbe());
		var changes = 0;
		service.Changed += (_, _) => changes++;

		_store.Update<AppearanceSettings>(a => a.LineSpacing = 1.6);

		Assert.Equal(1, changes);
		Assert.Contains("\"lh\":1.6", PageAppearance.From(false, service.Current).ToMessage());
	}

	// TC.LS.05
	[Fact]
	public void ThePageUsesOneVariableForEveryRow_AndTheHostSetsIt()
	{
		var web = Path.Combine(AppContext.BaseDirectory, "Web");
		var html = File.ReadAllText(Path.Combine(web, "transcript.html"));
		var js = File.ReadAllText(Path.Combine(web, "transcript.js"));

		Assert.Matches(@"--lh:1\.25[;}]", html);
		Assert.Matches(@"(?m)^html,body\{[^}]*font:14px/var\(--lh\)", html);
		Assert.DoesNotContain("1.45", html);
		Assert.Contains("--lh", js);
		Assert.Matches(new Regex(@"m\.lh"), js);
	}

	private static void Layout(FrameworkElement element)
	{
		element.Measure(new Size(1100, 700));
		element.Arrange(new Rect(0, 0, 1100, 700));
		element.UpdateLayout();
	}

	private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
	{
		var count = VisualTreeHelper.GetChildrenCount(root);
		for (var i = 0; i < count; i++)
		{
			var child = VisualTreeHelper.GetChild(root, i);
			if (child is T match)
				yield return match;
			foreach (var nested in Descendants<T>(child))
				yield return nested;
		}
	}

	// TC.LS.06
	[Fact]
	public void TheFocusCardText_FollowsTheSpacing_AndChangesLive()
	{
		var rows = new List<(string Text, double Size, double Height, LineStackingStrategy Strategy)>();
		var narrow = new List<(string Text, double Size, double Height)>();
		var bodyParagraphs = 0;
		var wideHeight = 0.0;
		var tightHeight = 0.0;

		StaTestRunner.Run(() =>
		{
			var t0 = DateTimeOffset.UnixEpoch;
			var client = new FakeTessynClient();
			client.Attention["a"] =
			[
				new AttentionItemDto { Id = "A1", Number = 1, Kind = AttentionKind.Ask, Status = AttentionStatus.Open, Title = "Which database?", Body = "Pick **one** of them", CreatedUtc = t0 },
				new AttentionItemDto { Id = "A2", Number = 2, Kind = AttentionKind.Ask, Status = AttentionStatus.Answered, Title = "Closed question", Body = "Closed body", Answer = "Postgres", CreatedUtc = t0, ClosedUtc = t0.AddMinutes(5) },
			];
			var focus = new FocusViewModel(client, new ImmediateUiDispatcher(), NullLogger<FocusViewModel>.Instance);
			focus.SetSessionAsync("a").GetAwaiter().GetResult();
			var panel = new FocusPanel { DataContext = focus };
			panel.Resources[Constants.Appearance.LineSpacingKey] = 1.5;
			Layout(panel);
			focus.IsClosedExpanded = true;
			focus.ClosedItems.Single().ToggleBodyCommand.Execute(null);
			Layout(panel);

			static string Shown(TextBlock t) => new System.Windows.Documents.TextRange(t.ContentStart, t.ContentEnd).Text;
			bool Wanted(TextBlock t) => Shown(t) is "Which database?" or "Closed question" or "Answered: Postgres" || Shown(t).StartsWith("Pick", StringComparison.Ordinal) || Shown(t).StartsWith("Closed body", StringComparison.Ordinal);
			foreach (var t in Descendants<TextBlock>(panel).Where(Wanted))
				rows.Add((Shown(t), t.FontSize, t.LineHeight, t.LineStackingStrategy));
			bodyParagraphs = rows.Count(r => r.Text.StartsWith("Pick", StringComparison.Ordinal) || r.Text.StartsWith("Closed body", StringComparison.Ordinal));
			wideHeight = panel.DesiredSize.Height;

			panel.Resources[Constants.Appearance.LineSpacingKey] = 1.0;
			Layout(panel);
			foreach (var t in Descendants<TextBlock>(panel).Where(Wanted))
				narrow.Add((Shown(t), t.FontSize, t.LineHeight));
			tightHeight = panel.DesiredSize.Height;
		});

		foreach (var needed in new[] { "Which database?", "Closed question", "Answered: Postgres" })
			Assert.Contains(rows, r => r.Text == needed);   // the titles and the outcome
		Assert.True(bodyParagraphs >= 1, "no body text found");
		Assert.All(rows, r =>
		{
			Assert.Equal(r.Size * 1.5, r.Height, 3);
			Assert.Equal(LineStackingStrategy.BlockLineHeight, r.Strategy);
		});
		Assert.All(narrow, r => Assert.Equal(r.Size * 1.0, r.Height, 3));
		Assert.True(wideHeight > tightHeight, $"the panel did not get shorter with tighter spacing: {wideHeight} vs {tightHeight}");
	}
}
