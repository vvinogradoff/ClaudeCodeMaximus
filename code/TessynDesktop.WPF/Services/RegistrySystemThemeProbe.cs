using Microsoft.Win32;

namespace TessynDesktop.WPF.Services;

/// <summary>Reads <c>AppsUseLightTheme</c> from the current user's personalization key and follows its changes.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class RegistrySystemThemeProbe : ISystemThemeProbe, IDisposable
{
	private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
	private const string ValueName = "AppsUseLightTheme";

	private bool _subscribed;
	private EventHandler? _changed;

	public bool IsLight
	{
		get
		{
			try
			{
				using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
				return key?.GetValue(ValueName) is not int value || value != 0;
			}
			catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
			{
				return true;
			}
		}
	}

	public event EventHandler? Changed
	{
		add
		{
			_changed += value;
			if (!_subscribed)
			{
				_subscribed = true;
				SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
			}
		}
		remove => _changed -= value;
	}

	private void OnPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
	{
		if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
			_changed?.Invoke(this, EventArgs.Empty);
	}

	public void Dispose()
	{
		if (_subscribed)
			SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
	}
}
