using System.Windows;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// FR.9.14 / TC.TH.01: the stored theme is on the window from the first paint. Windows 11 apps that start with a window of the system's (dark) look while the
/// stored theme is Light showed a black window with light controls until the theme was switched back and forth; the window is now pinned to the stored theme when it is created, again
/// after its first frame, and on every theme change.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class FirstPaintThemeTests
{
	private sealed class DarkSystemProbe : ISystemThemeProbe
	{
		public bool IsLight => false;

		public event EventHandler? Changed
		{
			add { }
			remove { }
		}
	}

#pragma warning disable WPF0001 // ThemeMode is the supported way to select the Fluent variant
	[Fact]
	public void TheWindowStyling_PutsTheStoredThemeOnTheWindow_WhateverItWasCreatedUnder()
	{
		StaTestRunner.Run(() =>
		{
			var store = new ClientSettingsStore(new MemoryClientState(), NullLogger<ClientSettingsStore>.Instance);
			store.Update<AppearanceSettings>(a => a.Theme = ClientTheme.Light);
			var appearance = new AppearanceService(store, new DarkSystemProbe());
			var applier = new AppearanceApplier(appearance, new ImmediateUiDispatcher());
			var window = new Window { ThemeMode = ThemeMode.Dark };   // what a window gets when the system is dark

			applier.ApplyToWindow(window);
			Assert.Equal(ThemeMode.Light, window.ThemeMode);

			store.Update<AppearanceSettings>(a => a.Theme = ClientTheme.Dark);
			applier.ApplyToWindow(window);
			Assert.Equal(ThemeMode.Dark, window.ThemeMode);

			store.Update<AppearanceSettings>(a => a.Theme = ClientTheme.System);
			applier.ApplyToWindow(window);
			Assert.Equal(ThemeMode.System, window.ThemeMode);
		});
	}
#pragma warning restore WPF0001
}
