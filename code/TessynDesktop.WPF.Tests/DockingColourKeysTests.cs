using System.Reflection;
using System.Windows;
using System.Windows.Media;
using AvalonDock.Themes.VS2013.Themes;
using AvalonDock.Themes.VS2013.Themes.Menu;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>FR.6.21: both colour dictionaries of the docking chrome define every key of the VS2013 theme (the library's templates are loaded without its own brushes, so a missing key is a blank or default brush).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class DockingColourKeysTests
{
	private static ResourceDictionary Load(string mode)
	{
		_ = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };   // the pack: scheme the dictionary's address needs
		return new ResourceDictionary { Source = new Uri($"/TessynDesktop.WPF;component/Styles/DockingColours.{mode}.xaml", UriKind.Relative) };
	}

	/// <summary>Every public static key of <see cref="ResourceKeys"/> (the tabs, panes, floating windows) and of <see cref="MenuKeys"/> (the menus and the tab dropdown).</summary>
	private static IReadOnlyList<(string Name, object Key)> ThemeKeys() =>
		new[] { typeof(ResourceKeys), typeof(MenuKeys) }
			.SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.GetValue(null) != null).Select(f => (t.Name + "." + f.Name, f.GetValue(null)!)))
			.ToList();

	/// <summary>The keys the library's own template dictionaries define (sizes, not colours): they do not change with the mode and are not Tessyn's to supply.</summary>
	private static HashSet<object> SuppliedByTheTemplates()
	{
		var supplied = new HashSet<object>();
		void Collect(ResourceDictionary d)
		{
			foreach (var key in d.Keys)
				supplied.Add(key);
			foreach (var merged in d.MergedDictionaries)
				Collect(merged);
		}

		Collect(new TessynDockTheme().ThemeResourceDictionary);
		return supplied;
	}

	[Theory]
	[InlineData("Dark")]
	[InlineData("Light")]
	public void EveryKeyOfTheVs2013Theme_IsDefined_ByTheColourDictionary_OrByTheLibrarysTemplatesAsANonColour(string mode)
	{
		StaTestRunner.Run(() =>
		{
			var keys = ThemeKeys();
			var dictionary = Load(mode);
			var templates = SuppliedByTheTemplates();

			Assert.NotEmpty(keys);
			var missing = keys.Where(k => !dictionary.Contains(k.Key) && !templates.Contains(k.Key)).Select(k => k.Name).ToList();
			Assert.True(missing.Count == 0, $"{mode} lacks: {string.Join(", ", missing)}");
		});
	}

	[Fact]
	public void WhatTheTemplatesSupply_IsNoColour_ABrushThere_WouldNotFollowTheMode()
	{
		StaTestRunner.Run(() =>
		{
			var dictionary = Load("Dark");
			var templates = SuppliedByTheTemplates();

			var colours = ThemeKeys().Where(k => !dictionary.Contains(k.Key) && templates.Contains(k.Key)).Select(k => k.Name).ToList();

			Assert.Equal(["ResourceKeys.DockingButtonWidthKey", "ResourceKeys.DockingButtonHeightKey"], colours);   // the two sizes of the drop-guide buttons
		});
	}

	[Theory]
	[InlineData("Dark")]
	[InlineData("Light")]
	public void TheMenuAndDropdownKeysAdded_AreBrushes(string mode)
	{
		StaTestRunner.Run(() =>
		{
			var dictionary = Load(mode);
			foreach (var name in new[]
			{
				nameof(MenuKeys.CheckMarkBackgroundBrushKey), nameof(MenuKeys.CheckMarkBorderBrushKey), nameof(MenuKeys.CheckMarkForegroundBrushKey),
				nameof(MenuKeys.DisabledSubMenuItemBackgroundBrushKey), nameof(MenuKeys.DisabledSubMenuItemBorderBrushKey), nameof(MenuKeys.FocusScrollButtonBrushKey),
				nameof(MenuKeys.MenuItemHighlightedBackgroundKey), nameof(MenuKeys.MenuSeparatorBorderBrushKey), nameof(MenuKeys.NormalBackgroundBrushKey),
				nameof(MenuKeys.TopLevelHeaderMenuBackgroundKey),
			})
			{
				var key = typeof(MenuKeys).GetField(name)!.GetValue(null)!;
				Assert.IsType<SolidColorBrush>(dictionary[key]);
			}
		});
	}
}
