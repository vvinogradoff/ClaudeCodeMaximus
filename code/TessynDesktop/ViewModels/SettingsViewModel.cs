using System;
using System.Collections.ObjectModel;
using System.Reactive;
using TessynDesktop.Models;
using TessynDesktop.Services;
using ReactiveUI;

namespace TessynDesktop.ViewModels;

/// <remarks>Created by Claude</remarks>
public sealed class SettingsViewModel : ViewModelBase
{
	private readonly IAppSettingsService _appSettings;
	private string _sessionFilesRoot;
	private string _claudePath;
	private string _sourceCodesLocation;
	private bool _isDarkTheme;

	// Color fields for the currently selected theme
	private string _inputBoxBackground;
	private string _inputBoxText;
	private string _userBubbleBackground;
	private string _userBubbleText;
	private string _codeBlockBackground;
	private string _codeBlockText;
	private string _inlineCodeBackground;
	private string _inlineCodeText;
	private string _systemBubbleBackground;
	private string _recency15MinBackground;
	private string _recency30MinBackground;
	private string _recency60MinBackground;

	public string SessionFilesRoot
	{
		get => _sessionFilesRoot;
		set => this.RaiseAndSetIfChanged(ref _sessionFilesRoot, value);
	}

	public string ClaudePath
	{
		get => _claudePath;
		set => this.RaiseAndSetIfChanged(ref _claudePath, value);
	}

	public string SourceCodesLocation
	{
		get => _sourceCodesLocation;
		set => this.RaiseAndSetIfChanged(ref _sourceCodesLocation, value);
	}

	public bool IsDarkTheme
	{
		get => _isDarkTheme;
		set
		{
			this.RaiseAndSetIfChanged(ref _isDarkTheme, value);
			LoadColorsFromTheme();
		}
	}

	public string InputBoxBackground
	{
		get => _inputBoxBackground;
		set => this.RaiseAndSetIfChanged(ref _inputBoxBackground, value);
	}

	public string InputBoxText
	{
		get => _inputBoxText;
		set => this.RaiseAndSetIfChanged(ref _inputBoxText, value);
	}

	public string UserBubbleBackground
	{
		get => _userBubbleBackground;
		set => this.RaiseAndSetIfChanged(ref _userBubbleBackground, value);
	}

	public string UserBubbleText
	{
		get => _userBubbleText;
		set => this.RaiseAndSetIfChanged(ref _userBubbleText, value);
	}

	public string CodeBlockBackground
	{
		get => _codeBlockBackground;
		set => this.RaiseAndSetIfChanged(ref _codeBlockBackground, value);
	}

	public string CodeBlockText
	{
		get => _codeBlockText;
		set => this.RaiseAndSetIfChanged(ref _codeBlockText, value);
	}

	public string InlineCodeBackground
	{
		get => _inlineCodeBackground;
		set => this.RaiseAndSetIfChanged(ref _inlineCodeBackground, value);
	}

	public string InlineCodeText
	{
		get => _inlineCodeText;
		set => this.RaiseAndSetIfChanged(ref _inlineCodeText, value);
	}

	public string SystemBubbleBackground
	{
		get => _systemBubbleBackground;
		set => this.RaiseAndSetIfChanged(ref _systemBubbleBackground, value);
	}

	public string Recency15MinBackground
	{
		get => _recency15MinBackground;
		set => this.RaiseAndSetIfChanged(ref _recency15MinBackground, value);
	}

	public string Recency30MinBackground
	{
		get => _recency30MinBackground;
		set => this.RaiseAndSetIfChanged(ref _recency30MinBackground, value);
	}

	public string Recency60MinBackground
	{
		get => _recency60MinBackground;
		set => this.RaiseAndSetIfChanged(ref _recency60MinBackground, value);
	}

	public ReactiveCommand<Unit, Unit> SaveCommand { get; }

	/// <summary>
	/// Key bindings displayed in the settings window.
	/// Each entry has an action name and its current binding string.
	/// </summary>
	public ObservableCollection<KeyBindingEntryViewModel> KeyBindingEntries { get; } = [];

	public SettingsViewModel(IAppSettingsService appSettings)
	{
		_appSettings = appSettings;
		_sessionFilesRoot = appSettings.Settings.SessionFilesRoot;
		_claudePath = appSettings.Settings.ClaudePath;
		_sourceCodesLocation = appSettings.Settings.SourceCodesLocation;
		_isDarkTheme = appSettings.Settings.Theme == "Dark";

		var colors = _isDarkTheme ? appSettings.Settings.DarkColors : appSettings.Settings.LightColors;
		_inputBoxBackground    = colors.InputBoxBackground;
		_inputBoxText          = colors.InputBoxText;
		_userBubbleBackground  = colors.UserBubbleBackground;
		_userBubbleText        = colors.UserBubbleText;
		_codeBlockBackground   = colors.CodeBlockBackground;
		_codeBlockText         = colors.CodeBlockText;
		_inlineCodeBackground  = colors.InlineCodeBackground;
		_inlineCodeText        = colors.InlineCodeText;
		_systemBubbleBackground = colors.SystemBubbleBackground;
		_recency15MinBackground = colors.Recency15MinBackground;
		_recency30MinBackground = colors.Recency30MinBackground;
		_recency60MinBackground = colors.Recency60MinBackground;

		SaveCommand = ReactiveCommand.Create(Save);

		LoadKeyBindings();
	}

	private void LoadKeyBindings()
	{
		KeyBindingEntries.Clear();
		foreach (var (action, binding) in _appSettings.Settings.KeyBindings.Bindings)
			KeyBindingEntries.Add(new KeyBindingEntryViewModel(action, binding));
	}

	private void LoadColorsFromTheme()
	{
		var colors = _isDarkTheme ? _appSettings.Settings.DarkColors : _appSettings.Settings.LightColors;
		InputBoxBackground    = colors.InputBoxBackground;
		InputBoxText          = colors.InputBoxText;
		UserBubbleBackground  = colors.UserBubbleBackground;
		UserBubbleText        = colors.UserBubbleText;
		CodeBlockBackground   = colors.CodeBlockBackground;
		CodeBlockText         = colors.CodeBlockText;
		InlineCodeBackground  = colors.InlineCodeBackground;
		InlineCodeText        = colors.InlineCodeText;
		SystemBubbleBackground = colors.SystemBubbleBackground;
		Recency15MinBackground = colors.Recency15MinBackground;
		Recency30MinBackground = colors.Recency30MinBackground;
		Recency60MinBackground = colors.Recency60MinBackground;
	}

	private void Save()
	{
		_appSettings.Settings.SessionFilesRoot = _sessionFilesRoot;
		_appSettings.Settings.ClaudePath = _claudePath;
		_appSettings.Settings.SourceCodesLocation = _sourceCodesLocation;
		_appSettings.Settings.Theme = _isDarkTheme ? "Dark" : "Light";

		// Save colors to the appropriate theme
		var colors = _isDarkTheme ? _appSettings.Settings.DarkColors : _appSettings.Settings.LightColors;
		colors.InputBoxBackground    = _inputBoxBackground;
		colors.InputBoxText          = _inputBoxText;
		colors.UserBubbleBackground  = _userBubbleBackground;
		colors.UserBubbleText        = _userBubbleText;
		colors.CodeBlockBackground   = _codeBlockBackground;
		colors.CodeBlockText         = _codeBlockText;
		colors.InlineCodeBackground  = _inlineCodeBackground;
		colors.InlineCodeText        = _inlineCodeText;
		colors.SystemBubbleBackground = _systemBubbleBackground;
		colors.Recency15MinBackground = _recency15MinBackground;
		colors.Recency30MinBackground = _recency30MinBackground;
		colors.Recency60MinBackground = _recency60MinBackground;

		// Save key bindings
		foreach (var entry in KeyBindingEntries)
			_appSettings.Settings.KeyBindings.Bindings[entry.ActionName] = entry.Binding;

		ThemeApplicator.Apply(_appSettings.Settings);
		_appSettings.Save();
	}
}
