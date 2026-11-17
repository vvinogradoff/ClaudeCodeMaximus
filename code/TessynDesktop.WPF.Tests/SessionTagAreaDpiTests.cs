using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// FR.24.16 / TC.TA.21: the mini line holds at every monitor scale. The Architect's monitor runs at 200% (and laptops at 125/150/175%), where layout rounding snaps
/// chip heights and line offsets to device pixels; at 96 DPI everything is whole numbers and hides rounding errors. Real layout, rounding on, many widths and font sizes.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionTagAreaDpiTests
{
	private static SessionTagViewModel Chip(int index) =>
		new(new SessionTagDto { Kind = index % 9 == 0 ? SessionTagKind.GitHubRepository : SessionTagKind.GitHubPullRequest, Label = (index % 9 == 0 ? "backend-v" : "backend-v1 PR #") + (100 + index), Url = "https://github.com/o/r/pull/" + index },
			new FakeUrlLauncher());

	private static readonly (SessionTagKind Kind, string Label)[] RealTags =
	[
		(SessionTagKind.GitHubRepository, "windows-app"), (SessionTagKind.LinearIssue, "HSE-275"), (SessionTagKind.GitHubPullRequest, "windows-app PR #292"), (SessionTagKind.LinearIssue, "HSE-347"),
		(SessionTagKind.GitHubRepository, "setup"), (SessionTagKind.GitHubRepository, "backend-v1"), (SessionTagKind.GitHubPullRequest, "backend-v1 PR #208"), (SessionTagKind.GitHubPullRequest, "windows-app PR #293"),
	];

	private static (SessionTagArea Area, HwndSource Source, TagAreaViewModel Model) Create(double dpi, double width, double fontSize, int tags, double lineSpacing)
	{
		var collection = new ObservableCollection<SessionTagViewModel>(tags == 8 ? RealTags.Select(t => new SessionTagViewModel(new SessionTagDto { Kind = t.Kind, Label = t.Label, Url = "https://example.com/" + t.Label }, new FakeUrlLauncher())) : Enumerable.Range(0, tags).Select(Chip));
		var model = new TagAreaViewModel(collection);
		var area = new SessionTagArea { DataContext = model, TagFontSize = fontSize };
		LineSpacing.SetFactor(area, lineSpacing);
		var root = new Border { Width = width, VerticalAlignment = VerticalAlignment.Top, Child = area, UseLayoutRounding = true };
		var source = new HwndSource(new HwndSourceParameters("tag-area-dpi-test", (int)width, 600) { WindowStyle = 0 }) { SizeToContent = SizeToContent.Height };
		VisualTreeHelper.SetRootDpi(root, new DpiScale(dpi, dpi));
		source.RootVisual = root;
		Settle(root);
		return (area, source, model);
	}

	private static void Settle(FrameworkElement element)
	{
		for (var i = 0; i < 8; i++)
		{
			element.UpdateLayout();
			Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Render, () => { });
		}
	}

	[Fact]
	public void TheMiniLine_IsExactlyOneLineWithTheHiddenCount_AtEveryMonitorScale()
	{
		StaTestRunner.Run(() =>
		{
			var failures = new List<string>();
			foreach (var count in new[] { 2, 8, 12, 63 })
			foreach (var dpi in new[] { 1.0, 1.5, 2.0 })
			foreach (var font in new[] { 12.0 })
			foreach (var spacing in new[] { 1.25 })
			for (var width = 380.0; width <= 1100; width += 131)
			{
				var (area, source, _) = Create(dpi, width, font, tags: count, spacing);
				try
				{
					var panel = FindPanel(area)!;
					var chips = panel.Children.Cast<FrameworkElement>().ToList();
					var firstLine = chips.Count(c => c.TranslatePoint(default, panel).Y < 0.5);
					var tag = $"tags {count} dpi {dpi} font {font} spacing {spacing} width {width}";
					if (area.LineHeight <= 0 || Math.Abs(area.AreaHeight - area.LineHeight) > 0.51)
						failures.Add($"{tag}: area {area.AreaHeight} vs line {area.LineHeight}");
					else if (area.HiddenCount != chips.Count - firstLine || (chips.Count > firstLine && Math.Abs(area.AreaHeight - area.LineHeight) > 0.51))
						failures.Add($"{tag}: hidden {area.HiddenCount} vs {chips.Count - firstLine} chips below the first line");
				}
				finally
				{
					source.Dispose();
				}
			}

			Assert.True(failures.Count == 0, $"{failures.Count} layouts are wrong, first: {string.Join(" | ", failures.Take(6))}");
		});
	}

	/// <summary>
	/// A session switch in the app: the old chips are cleared and laid out empty, then the next session's chips arrive, all in one go, onto an area that
	/// had a mini height before. TC.TA.20.
	/// </summary>
	[Fact]
	public void AfterASessionSwitch_TheNewChipsCollapseToOneLine_WhateverWasShownBefore()
	{
		StaTestRunner.Run(() =>
		{
			var failures = new List<string>();
			foreach (var before in new[] { 0, 1, 3, 63 })
			foreach (var layoutWhenEmpty in new[] { false, true })
			foreach (var dpi in new[] { 1.0, 2.0 })
			{
				var (area, source, model) = Create(dpi, 420, 12, before, 1.25);
				try
				{
					var tags = model.Tags;
					tags.Clear();
					if (layoutWhenEmpty)
						Settle((FrameworkElement)VisualTreeHelper.GetParent(area));

					foreach (var t in RealTags)
						tags.Add(new SessionTagViewModel(new SessionTagDto { Kind = t.Kind, Label = t.Label, Url = "https://example.com/" + t.Label }, new FakeUrlLauncher()));

					Settle((FrameworkElement)VisualTreeHelper.GetParent(area));
					var panel = FindPanel(area)!;
					var rows = panel.Children.Cast<FrameworkElement>().Select(c => Math.Round(c.TranslatePoint(default, panel).Y)).Distinct().Count();
					var tag = $"before {before} emptyLayout {layoutWhenEmpty} dpi {dpi}";
					if (rows < 2 || Math.Abs(area.AreaHeight - area.LineHeight) > 0.51 || area.HiddenCount == 0)
						failures.Add($"{tag}: area {area.AreaHeight} line {area.LineHeight} hidden {area.HiddenCount} rows {rows}");
				}
				finally
				{
					source.Dispose();
				}
			}

			Assert.True(failures.Count == 0, string.Join(" | ", failures));
		});
	}

	private static Panel? FindPanel(DependencyObject root)
	{
		for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
		{
			var child = VisualTreeHelper.GetChild(root, i);
			if (child is Panel { IsItemsHost: true } panel)
				return panel;

			var nested = FindPanel(child);
			if (nested != null)
				return nested;
		}

		return null;
	}
}
