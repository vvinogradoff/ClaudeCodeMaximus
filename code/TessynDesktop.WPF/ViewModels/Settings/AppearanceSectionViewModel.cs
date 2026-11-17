using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.ViewModels.Settings;

/// <summary>
/// The Appearance tab (FR.9): the theme, the four font sizes and the tag cloud font size, stored in the client settings (<c>wpf.json</c>, section <c>appearance</c>). Applying them is the theme
/// package's job; this tab only edits the stored values. The colour palette editor of the Avalonia client is dropped (decision A8).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class AppearanceSectionViewModel : SettingsSectionViewModel, ISettingsEditor
{
	private readonly IClientSettingsStore _store;

	[ObservableProperty]
	private ClientTheme _theme;

	[ObservableProperty]
	private SessionDateFormat _dateFormat;

	[ObservableProperty]
	private string _assistantFontSize = string.Empty;

	[ObservableProperty]
	private string _assistantMarkdownFontSize = string.Empty;

	[ObservableProperty]
	private string _userFontSize = string.Empty;

	[ObservableProperty]
	private string _inputFontSize = string.Empty;

	[ObservableProperty]
	private string _tagFontSize = string.Empty;

	public AppearanceSectionViewModel(IClientSettingsStore store)
	{
		_store = store;
		Read();
	}

	public override string Title => "Appearance";

	public IReadOnlyList<SettingOption<ClientTheme>> Themes { get; } =
	[
		new(ClientTheme.System, Constants.Settings.ThemeSystem),
		new(ClientTheme.Light, Constants.Settings.ThemeLight),
		new(ClientTheme.Dark, Constants.Settings.ThemeDark),
	];

	public IReadOnlyList<SettingOption<SessionDateFormat>> DateFormats { get; } =
	[
		new(SessionDateFormat.Absolute, Constants.DateFormats.AbsoluteLabel),
		new(SessionDateFormat.Relative, Constants.DateFormats.RelativeLabel),
	];

	public bool HasChanges
	{
		get
		{
			try
			{
				var stored = _store.Get<AppearanceSettings>();
				var edited = Build(stored);
				return edited.Theme != stored.Theme
					|| edited.DateFormat != stored.DateFormat
					|| edited.AssistantFontSize != stored.AssistantFontSize
					|| edited.AssistantMarkdownFontSize != stored.AssistantMarkdownFontSize
					|| edited.UserFontSize != stored.UserFontSize
					|| edited.InputFontSize != stored.InputFontSize
					|| edited.TagFontSize != stored.TagFontSize;
			}
			catch (SettingsInputException)
			{
				return true;
			}
		}
	}

	public Task<bool> SaveAsync()
	{
		ClearMessages();
		try
		{
			var edited = Build(_store.Get<AppearanceSettings>());
			_store.Update<AppearanceSettings>(a =>
			{
				a.Theme = edited.Theme;
				a.DateFormat = edited.DateFormat;
				a.AssistantFontSize = edited.AssistantFontSize;
				a.AssistantMarkdownFontSize = edited.AssistantMarkdownFontSize;
				a.UserFontSize = edited.UserFontSize;
				a.InputFontSize = edited.InputFontSize;
				a.TagFontSize = edited.TagFontSize;
			});
			StatusText = Constants.Settings.Saved;
			return Task.FromResult(true);
		}
		catch (SettingsInputException ex)
		{
			ErrorText = string.Format(Constants.Settings.SaveFailedFormat, ex.Message);
			return Task.FromResult(false);
		}
	}

	[RelayCommand]
	public Task ReloadAsync()
	{
		ClearMessages();
		Read();
		return Task.CompletedTask;
	}

	/// <summary>Reads the stored values into the editor (also after an import).</summary>
	public void Read()
	{
		var stored = _store.Get<AppearanceSettings>();
		Theme = stored.Theme;
		DateFormat = stored.DateFormat;
		AssistantFontSize = Format(stored.AssistantFontSize);
		AssistantMarkdownFontSize = Format(stored.AssistantMarkdownFontSize);
		UserFontSize = Format(stored.UserFontSize);
		InputFontSize = Format(stored.InputFontSize);
		TagFontSize = Format(stored.TagFontSize);
	}

	private AppearanceSettings Build(AppearanceSettings stored) => new()
	{
		Theme = Theme,
		DateFormat = DateFormat,
		AssistantFontSize = ParseSize(AssistantFontSize, stored.AssistantFontSize),
		AssistantMarkdownFontSize = ParseSize(AssistantMarkdownFontSize, stored.AssistantMarkdownFontSize),
		UserFontSize = ParseSize(UserFontSize, stored.UserFontSize),
		InputFontSize = ParseSize(InputFontSize, stored.InputFontSize),
		TagFontSize = ParseSize(TagFontSize, stored.TagFontSize),
	};

	private static double ParseSize(string text, double fallback)
	{
		if (string.IsNullOrWhiteSpace(text))
			return fallback;

		if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var size)
			|| size < Constants.Settings.MinFontSize || size > Constants.Settings.MaxFontSize)
			throw new SettingsInputException(string.Format(Constants.Settings.FontSizeInvalid, Constants.Settings.MinFontSize, Constants.Settings.MaxFontSize));

		return size;
	}

	private static string Format(double size) => size.ToString("0.##", CultureInfo.InvariantCulture);
}
