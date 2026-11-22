using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// The ChatGPT profiles of Settings > Profiles (FR.33.5, FR.33.17, tdcp-spec 10.A14): the list from <c>profile.list</c>'s <c>codexProfiles</c>, and
/// <b>New ChatGPT profile…</b>, <b>Sign in again</b>, <b>Sign out</b> and <b>Delete</b>. A sign-in asks how (browser or code), starts
/// <c>profiles.codexLogin</c>, opens the address in the system browser for the browser way (FR.26.9), shows <see cref="SignIn"/> until
/// <c>profiles.codexLoginCompleted</c> with its login id arrives, and can be cancelled.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class ProfileViewModel
{
	private readonly IUrlLauncher? _urlLauncher;
	private readonly IClipboardWriter? _clipboard;
	private readonly Dictionary<string, ProfileCodexLoginCompletedPayload> _earlyCompletions = new();

	/// <summary>The daemon offers ChatGPT profiles (it announces <c>profiles.codex</c> and lists <c>codexProfiles</c>).</summary>
	[ObservableProperty]
	private bool _isChatGptAvailable;

	/// <summary>The sign-in in progress; null when none.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsSigningIn))]
	[NotifyCanExecuteChangedFor(nameof(NewChatGptProfileCommand))]
	[NotifyCanExecuteChangedFor(nameof(CancelSignInCommand))]
	private ChatGptSignInViewModel? _signIn;

	public ObservableCollection<ChatGptProfileRowViewModel> ChatGptProfiles { get; } = [];

	public bool IsSigningIn => SignIn != null;

	public string CodexDefaultWarning => Constants.Codex.CodexDefaultWarning;

	private bool HasChatGptCapability => _client?.ServerInfo?.Capabilities.Contains(RemoteProtocol.CodexProfilesAdmin.Capability) == true;

	/// <summary>Puts the ChatGPT profiles of a <c>profile.list</c> answer in place (called on the UI thread).</summary>
	private void ApplyChatGptProfiles(IReadOnlyList<CodexProfileDto>? profiles)
	{
		ChatGptProfiles.Clear();
		foreach (var profile in profiles ?? [])
			ChatGptProfiles.Add(new ChatGptProfileRowViewModel(profile));

		IsChatGptAvailable = profiles != null && HasChatGptCapability;
	}

	private bool CanStartSignIn() => SignIn == null;

	/// <summary>New ChatGPT profile…: a new <c>codex_N</c> is added only when the sign-in succeeds.</summary>
	[RelayCommand(CanExecute = nameof(CanStartSignIn))]
	public Task NewChatGptProfileAsync() => StartSignInAsync(null, Constants.Codex.NewChatGptTitle);

	/// <summary>Sign in again: the same profile signs in anew (after an auth error or a sign-out, FR.33.13).</summary>
	[RelayCommand]
	public Task SignInAgainAsync(ChatGptProfileRowViewModel? row) =>
		row == null || SignIn != null ? Task.CompletedTask : StartSignInAsync(row.Id, Constants.Codex.SignInAgainTitle);

	private async Task StartSignInAsync(string? profileId, string title)
	{
		var choice = await _dialogs.ChoiceAsync(new ChoiceDialogRequest(title, Constants.Codex.SignInPrompt,
			[new DialogChoice(Constants.Codex.BrowserChoiceId, Constants.Codex.BrowserChoiceLabel), new DialogChoice(Constants.Codex.CodeChoiceId, Constants.Codex.CodeChoiceLabel)]));
		if (choice == null)
			return;

		var mode = choice == Constants.Codex.CodeChoiceId ? RemoteProtocol.CodexProfilesAdmin.DeviceCodeMode : RemoteProtocol.CodexProfilesAdmin.BrowserMode;
		await RunAsync(async client =>
		{
			var login = await client.ProfileCodexLoginAsync(mode, profileId);
			var signIn = new ChatGptSignInViewModel(login, title, _clipboard, _urlLauncher);
			_ui.Post(() =>
			{
				SignIn = signIn;
				if (signIn.IsBrowser)
					signIn.OpenInBrowser();
				if (_earlyCompletions.Remove(login.LoginId, out var done))   // the sign-in ended before its answer was handled
					Complete(done);
			});
		});
	}

	private bool CanCancelSignIn() => SignIn != null;

	/// <summary>Cancel: the daemon ends the sign-in, and its failed completion closes the panel; nothing is added.</summary>
	[RelayCommand(CanExecute = nameof(CanCancelSignIn))]
	public async Task CancelSignInAsync()
	{
		var signIn = SignIn;
		if (signIn == null)
			return;

		await RunAsync(client => client.ProfileCodexLoginCancelAsync(signIn.LoginId));
		_ui.Post(() =>
		{
			if (ReferenceEquals(SignIn, signIn))   // no completion event came (an older daemon): close it anyway
				SignIn = null;
		});
	}

	[RelayCommand]
	public Task SignOutAsync(ChatGptProfileRowViewModel? row) =>
		row is not { CanSignOut: true }
			? Task.CompletedTask
			: RunAsync(async client =>
			{
				await client.ProfileCodexLogoutAsync(row.Id);
				_ui.Post(() => StatusText = string.Format(Constants.Codex.SignedOutFormat, row.DisplayName));
			});

	/// <summary>Delete, after a confirmation: the daemon stops the profile's Codex and renames its folder with <c>_rem</c> (nothing is deleted).</summary>
	[RelayCommand]
	public async Task DeleteChatGptAsync(ChatGptProfileRowViewModel? row)
	{
		if (row is not { CanDelete: true })
			return;

		var confirmed = await _dialogs.ConfirmAsync(new ConfirmDialogRequest(
			Constants.Codex.DeleteTitle, string.Format(Constants.Codex.ConfirmDeleteFormat, row.DisplayName), Constants.Codex.DeleteTitle, IsDestructive: true));
		if (!confirmed)
			return;

		await RunAsync(async client =>
		{
			var result = await client.ProfileRemoveAsync(row.Id);
			var text = result.PinnedSessionCount > 0
				? string.Format(Constants.Codex.DeletedPinnedFormat, row.DisplayName, result.PinnedSessionCount)
				: string.Format(Constants.Codex.DeletedFormat, row.DisplayName);
			_ui.Post(() => StatusText = text);
		});
	}

	private void OnCodexLoginCompleted(ProfileCodexLoginCompletedPayload payload) => _ui.Post(() =>
	{
		if (SignIn?.LoginId == payload.LoginId)
			Complete(payload);
		else if (SignIn == null)
			_earlyCompletions[payload.LoginId] = payload;   // our own call may not have been answered yet; another client's sign-in is only a refresh

		_ = RefreshAsync();
	});

	private void Complete(ProfileCodexLoginCompletedPayload payload)
	{
		SignIn = null;
		_earlyCompletions.Clear();
		if (payload.Success)
		{
			var plan = ChatGptProfileRowViewModel.PlanLabel(payload.Plan);
			StatusText = string.Format(Constants.Codex.SignedInFormat, payload.Email ?? payload.ProfileId, plan.Length == 0 ? string.Empty : $" ({plan})");
		}
		else
		{
			StatusText = string.Format(Constants.Codex.SignInFailedFormat, string.IsNullOrWhiteSpace(payload.Error) ? Constants.Codex.SignInFailedUnknown : payload.Error);
		}
	}
}
