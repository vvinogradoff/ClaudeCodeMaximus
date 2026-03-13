using System;
using System.Reactive;
using ClaudeMaximus.Models;
using ClaudeMaximus.Services;
using ReactiveUI;

namespace ClaudeMaximus.ViewModels;

/// <remarks>Created by Claude</remarks>
public sealed class SettingsViewModel : ViewModelBase
{
	private readonly IAppSettingsService _appSettings;
	private string _sessionFilesRoot;
	private string _claudePath;
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

	public ReactiveCommand<Unit, Unit> SaveCommand { get; }

	public SettingsViewModel(IAppSettingsService appSettings)
	{
		_appSettings = appSettings;
		_sessionFilesRoot = appSettings.Settings.SessionFilesRoot;
		_claudePath = appSettings.Settings.ClaudePath;
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

		SaveCommand = ReactiveCommand.Create(Save);
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
	}

	private void Save()
	{
		_appSettings.Settings.SessionFilesRoot = _sessionFilesRoot;
		_appSettings.Settings.ClaudePath = _claudePath;
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

		ThemeApplicator.Apply(_appSettings.Settings);
		_appSettings.Save();
	}
}
