using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// A ChatGPT sign-in in progress (FR.33.5), shown in Settings > Profiles until <c>profiles.codexLoginCompleted</c> ends it: the browser way (the address
/// was opened in the system browser, FR.26.9; it is shown too when the browser could not be opened) or the code way (the code and the address, each with
/// a copy button, and the note that sign-in with a code must be enabled in the ChatGPT security settings). Contains no WPF types.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class ChatGptSignInViewModel : ObservableObject
{
	private readonly IClipboardWriter? _clipboard;
	private readonly IUrlLauncher? _launcher;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(ShowsAddress))]
	private bool _browserOpenFailed;

	[ObservableProperty]
	private string? _copyStatus;

	public ChatGptSignInViewModel(ProfileCodexLoginResult login, string title, IClipboardWriter? clipboard, IUrlLauncher? launcher)
	{
		Login = login;
		Title = title;
		_clipboard = clipboard;
		_launcher = launcher;
	}

	public ProfileCodexLoginResult Login { get; }

	public string LoginId => Login.LoginId;

	public string ProfileId => Login.ProfileId;

	public string Title { get; }

	public bool IsDeviceCode => Login.Mode == RemoteProtocol.CodexProfilesAdmin.DeviceCodeMode;

	public bool IsBrowser => !IsDeviceCode;

	/// <summary>The code to enter (device-code sign-in).</summary>
	public string UserCode => Login.UserCode ?? string.Empty;

	/// <summary>Where to go: the verification address of a code sign-in, the sign-in address of a browser one.</summary>
	public string Address => (IsDeviceCode ? Login.VerificationUrl : Login.AuthUrl) ?? string.Empty;

	/// <summary>The address is shown for a code sign-in, and for a browser one whose browser did not open.</summary>
	public bool ShowsAddress => IsDeviceCode || BrowserOpenFailed;

	public string Instruction => IsDeviceCode ? Constants.Codex.CodeWaiting : Constants.Codex.BrowserWaiting;

	public string DeviceCodeNote => Constants.Codex.DeviceCodeNote;

	public string BrowserOpenFailedText => Constants.Codex.BrowserOpenFailed;

	/// <summary>Opens the address in the system browser; false when it could not be opened.</summary>
	public bool OpenInBrowser()
	{
		var opened = _launcher?.TryOpen(Address) == true;
		if (IsBrowser)
			BrowserOpenFailed = !opened;
		return opened;
	}

	[RelayCommand]
	private void OpenAddress() => OpenInBrowser();

	[RelayCommand]
	private void CopyCode() => Copy(UserCode);

	[RelayCommand]
	private void CopyAddress() => Copy(Address);

	private void Copy(string text) => CopyStatus = _clipboard?.TrySetText(text) == true ? Constants.Codex.Copied : Constants.Codex.CopyFailed;
}
