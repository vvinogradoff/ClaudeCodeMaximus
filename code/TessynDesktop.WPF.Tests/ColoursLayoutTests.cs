using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels.Settings;
using TessynDesktop.WPF.Views.Settings;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// FR.9.18 (UC.CL.04, TC.CL.20-25): the Colours tab's Apply button, the gap above the list, the two columns and the room before the scrollbar. The layout tests measure
/// the real positions of a real <see cref="ColoursSettingsView"/> on an STA thread; the Apply tests are on the view model and the applier.
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection(nameof(SessionDisplayCollection))]
public sealed class ColoursLayoutTests
{
	private sealed class FixedProbe : ISystemThemeProbe
	{
		public bool IsLight { get; set; } = true;

		public event EventHandler? Changed
		{
			add { }
			remove { }
		}
	}

	private static List<T> FindAll<T>(DependencyObject root) where T : DependencyObject
	{
		var found = new List<T>();
		var count = VisualTreeHelper.GetChildrenCount(root);
		for (var i = 0; i < count; i++)
		{
			var child = VisualTreeHelper.GetChild(root, i);
			if (child is T match)
				found.Add(match);

			found.AddRange(FindAll<T>(child));
		}

		return found;
	}

	private static void Layout(FrameworkElement element, double width, double height)
	{
		element.Measure(new Size(width, height));
		element.Arrange(new Rect(0, 0, width, height));
		element.UpdateLayout();
	}

	private static ColoursSectionViewModel NewTab(ClientSettingsStore store, out AppearanceService appearance, bool dark = false)
	{
		appearance = new AppearanceService(store, new FixedProbe { IsLight = !dark });
		store.Update<AppearanceSettings>(a => a.Theme = dark ? ClientTheme.Dark : ClientTheme.Light);
		return new ColoursSectionViewModel(store, appearance);
	}

	private static Rect BoundsIn(FrameworkElement element, FrameworkElement ancestor) => element.TransformToAncestor(ancestor).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

	private static Button ButtonNamed(DependencyObject root, string name) => FindAll<Button>(root).First(b => System.Windows.Automation.AutomationProperties.GetName(b) == name);

	private static ColoursSettingsView NewView(ColoursSectionViewModel tab, double width, double height)
	{
		var view = new ColoursSettingsView { DataContext = tab };
		Layout(view, width, height);
		return view;
	}

	// TC.CL.20
	[Fact]
	public void Apply_IsInTheThemeRow_AtTheRightContentEdge_RightOfResetAll()
	{
		var tab = NewTab(SettingsTestSupport.NewStore(), out _);
		StaTestRunner.Run(() =>
		{
			var view = NewView(tab, 900, 600);
			var apply = ButtonNamed(view, "Apply colours");
			var resetAll = ButtonNamed(view, "Reset all colours");
			var applyBounds = BoundsIn(apply, view);
			var resetBounds = BoundsIn(resetAll, view);

			Assert.Equal("Apply", apply.Content);
			Assert.True(applyBounds.Left >= resetBounds.Right, $"Apply {applyBounds} is not right of Reset all {resetBounds}");
			Assert.Equal(900 - 16, applyBounds.Right, 0.5);   // the tab's content edge: its 16 px right margin
			Assert.InRange(Math.Abs((applyBounds.Top + applyBounds.Height / 2) - (resetBounds.Top + resetBounds.Height / 2)), 0, 1.5);   // the same row
		});
	}

	// TC.CL.21
	[Fact]
	public void TheGap_BetweenTheThemeRowAndTheFirstGroupHeading_IsAtMost8Px_WithNoMessages()
	{
		var tab = NewTab(SettingsTestSupport.NewStore(), out _);
		StaTestRunner.Run(() =>
		{
			var view = NewView(tab, 900, 600);
			var resetBottom = BoundsIn(ButtonNamed(view, "Reset all colours"), view).Bottom;
			var heading = FindAll<TextBlock>(view).First(t => t.Text == ColourCatalog.All[0].Group);
			var gap = BoundsIn(heading, view).Top - resetBottom;

			Assert.InRange(gap, 0, 8);
		});
	}

	// TC.CL.22
	[Fact]
	public void TheRowsOfAGroup_AreInTwoEqualColumns_At900Px_AndInOneColumn_At600Px()
	{
		var tab = NewTab(SettingsTestSupport.NewStore(), out _);
		StaTestRunner.Run(() =>
		{
			var group = tab.Rows.Where(r => r.Group == tab.Rows[0].Group).Take(6).ToList();
			Assert.Equal(6, group.Count);

			var wide = NewView(tab, 900, 3000);
			var swatches = group.Select(r => BoundsIn(ButtonNamed(wide, r.AutomationName), wide)).ToList();
			for (var i = 0; i < 6; i += 2)
			{
				Assert.Equal(swatches[i].Top, swatches[i + 1].Top, 0.5);   // left, then right, row by row
				Assert.True(swatches[i + 1].Left > swatches[i].Right);
				if (i > 0)
				{
					Assert.True(swatches[i].Top > swatches[i - 2].Top);
					Assert.Equal(swatches[0].Left, swatches[i].Left, 0.5);
					Assert.Equal(swatches[1].Left, swatches[i + 1].Left, 0.5);
				}
			}

			var panels = FindAll<TwoColumnPanel>(wide);
			Assert.Equal(panels[0].ActualWidth / 2, swatches[1].Left - swatches[0].Left, 0.5);   // two equal columns

			var narrow = NewView(tab, 600, 3000);
			var single = group.Select(r => BoundsIn(ButtonNamed(narrow, r.AutomationName), narrow)).ToList();
			for (var i = 1; i < single.Count; i++)
			{
				Assert.True(single[i].Top > single[i - 1].Top, "one column: every row below the one before");
				Assert.Equal(single[0].Left, single[i].Left, 0.5);
			}
		});
	}

	// TC.CL.23
	[Theory]
	[InlineData(900)]
	[InlineData(600)]
	public void TheRightmostSwatchAndReset_EndAtLeast8PxBeforeTheScrollbar(double width)
	{
		var tab = NewTab(SettingsTestSupport.NewStore(), out _);
		foreach (var row in tab.Rows.Take(4))
			row.Set("#123456");   // every Reset button shows
		StaTestRunner.Run(() =>
		{
			var view = NewView(tab, width, 400);   // short, so the list scrolls
			var scrollbar = FindAll<ScrollBar>(view).First(s => s.Orientation == Orientation.Vertical && s.Visibility == Visibility.Visible);
			var scrollbarLeft = BoundsIn(scrollbar, view).Left;
			var rightmost = tab.Rows.Take(4)
				.SelectMany(r => new[] { r.AutomationName, r.ResetAutomationName })
				.Select(n => BoundsIn(ButtonNamed(view, n), view).Right)
				.Max();

			Assert.True(scrollbar.ActualWidth > 0);
			Assert.True(scrollbarLeft - rightmost >= 8, $"the rightmost button ends at {rightmost}, the scrollbar starts at {scrollbarLeft}");
		});
	}

	// TC.CL.24
	[Fact]
	public void Apply_CommitsTheOpenPickersColour_ClosesIt_AndReAppliesThePalette()
	{
		var store = SettingsTestSupport.NewStore();
		var tab = NewTab(store, out var appearance);
		var colourChanges = 0;
		var appearanceChanges = 0;
		store.Changed += (_, e) => colourChanges += e.Section == Constants.SettingsSections.Colours ? 1 : 0;
		appearance.Changed += (_, _) => appearanceChanges++;

		var row = tab.Rows.Single(r => r.Key == ColourCatalog.PageBackground);
		row.OpenPickerCommand.Execute(null);
		row.Picker.Show("#123456");   // the picker shows a colour the row has not taken yet
		Assert.True(row.IsPickerOpen);
		Assert.DoesNotContain(ColourCatalog.PageBackground, store.Get<ColourSettings>().Light.Keys);

		tab.ApplyCommand.Execute(null);
		row = tab.Rows.Single(r => r.Key == ColourCatalog.PageBackground);   // the rows are read again from the store

		Assert.False(row.IsPickerOpen);
		Assert.Equal("#123456", row.Hex);
		Assert.Equal("#123456", store.Get<ColourSettings>().Light[ColourCatalog.PageBackground]);
		Assert.Equal(1, colourChanges);   // the pending colours are stored in ONE update, which is also the re-apply
		Assert.True(appearanceChanges >= 1, "the transcript gets a fresh appearance message");
		Assert.Equal("#123456", appearance.PageColours["--bg"]);

		// With nothing open, Apply still re-applies (the same notification), and stores nothing new.
		var before = colourChanges;
		var stored = store.Get<ColourSettings>().Light.Count;
		tab.ApplyCommand.Execute(null);
		Assert.Equal(before + 1, colourChanges);
		Assert.Equal(stored, store.Get<ColourSettings>().Light.Count);
	}

	// TC.CL.25
	[Fact]
	public void Apply_OnTheThemeThatIsNotActive_StoresTheColours_AndLeavesTheActiveBrushesAlone()
	{
		var store = SettingsTestSupport.NewStore();
		var tab = NewTab(store, out var appearance);   // Light is active
		var applier = new ColourApplier(store, appearance, new ImmediateUiDispatcher());
		StaTestRunner.Run(() =>
		{
			tab.EditingDark = true;
			var row = tab.Rows.Single(r => r.Key == ColourCatalog.Text);
			var before = new ResourceDictionary();
			applier.Apply(before);

			row.OpenPickerCommand.Execute(null);
			row.Picker.Show("#ABCDEF");
			tab.ApplyCommand.Execute(null);

			Assert.Equal("#ABCDEF", store.Get<ColourSettings>().Dark[ColourCatalog.Text]);
			Assert.DoesNotContain(ColourCatalog.Text, store.Get<ColourSettings>().Light.Keys);
			var after = new ResourceDictionary();
			applier.Apply(after);
			foreach (var definition in ColourCatalog.All)
				Assert.Equal(((SolidColorBrush)before[definition.ResourceKey]).Color, ((SolidColorBrush)after[definition.ResourceKey]).Color);

			tab.EditingDark = false;   // and the Dark colour shows when Dark is active
			store.Update<AppearanceSettings>(a => a.Theme = ClientTheme.Dark);
			var dark = new ResourceDictionary();
			applier.Apply(dark);
			Assert.Equal(Color.FromRgb(0xAB, 0xCD, 0xEF), ((SolidColorBrush)dark[ColourCatalog.Find(ColourCatalog.Text)!.ResourceKey]).Color);
		});
	}
}
