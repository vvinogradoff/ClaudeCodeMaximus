using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// FR.33.5 and FR.33.17 in Settings > Profiles: the ChatGPT group with its plan and state, New ChatGPT profile… by browser (the address goes to the system
/// browser, FR.26.9) or by code (code and address with copy buttons and the device-code note), the end reported by <c>profiles.codexLoginCompleted</c>,
/// Cancel, Sign in again, Sign out and Delete with a confirmation, and the Codex Default warning (FR.33.6).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexProfilesSettingsTests
{
	private readonly FakeTessynClient _client = new();
	private readonly FakeDialogPresenter _presenter = new();
	private readonly FakeSystemUrlLauncher _browser = new();
	private readonly FakeClipboardWriter _clipboard = new();
	private readonly ProfileViewModel _profiles;

	public CodexProfilesSettingsTests()
	{
		_client.ServerInfo = new HelloResult(1, "test", "machine", "run", DateTimeOffset.UtcNow,
			[RemoteProtocol.ProfilesAdmin.Capability, RemoteProtocol.CodexProfilesAdmin.Capability], 1, 0);
		_client.Profiles = [new("default", "[Sys] me@example.com", true, "me@example.com", ProfileAuthState.Unknown, true), new("profile_1", "Work", false, null, ProfileAuthState.LoggedIn, false)];
		_client.CodexProfiles =
		[
			new("codex-default", "ChatGPT · me@example.com", "me@example.com", "plus", true, true, false, false),
			new("codex_1", "ChatGPT · u@example.com", "u@example.com", "plus", false, true, false, true),
			new("codex_2", "ChatGPT · w@example.com", "w@example.com", "pro", false, false, false, false),
		];
		var ui = new ImmediateUiDispatcher();
		_profiles = new ProfileViewModel(new DialogService(_presenter, ui), ui, NullLogger<ProfileViewModel>.Instance, _browser, _clipboard);
	}

	/// <summary>The next dialog picks this choice (or is cancelled when null).</summary>
	private void ChooseNext(string? choiceId) => _presenter.Answer = dialog =>
	{
		if (choiceId == null)
			dialog.CancelCommand.Execute(null);
		else if (dialog.IsChoice)
			dialog.ChooseCommand.Execute(dialog.Choices.Single(c => c.Id == choiceId));
		else
			dialog.AcceptCommand.Execute(null);
		return Task.CompletedTask;
	};

	[Fact]
	public async Task TheChatGptGroup_ListsItsProfiles_WithPlanAndState_AndCodexDefaultCannotBeDeleted()
	{
		await _profiles.AttachAsync(_client);

		Assert.True(_profiles.IsChatGptAvailable);
		Assert.Equal(["ChatGPT · me@example.com", "ChatGPT · u@example.com", "ChatGPT · w@example.com"], _profiles.ChatGptProfiles.Select(p => p.DisplayName));
		Assert.Equal(["ChatGPT Plus", "ChatGPT Plus", "ChatGPT Pro"], _profiles.ChatGptProfiles.Select(p => p.PlanText));
		Assert.Equal(["Signed in", "Signed in · model list", "Signed out"], _profiles.ChatGptProfiles.Select(p => p.StateText));
		Assert.False(_profiles.ChatGptProfiles[0].CanDelete);
		Assert.True(_profiles.ChatGptProfiles[0].IsCodexDefault);
		Assert.True(_profiles.ChatGptProfiles[1].CanDelete);
		Assert.False(_profiles.ChatGptProfiles[2].CanSignOut);
		Assert.Contains(@"%USERPROFILE%\.codex", _profiles.CodexDefaultWarning);
		Assert.Equal(["[Sys] me@example.com", "Work"], _profiles.Profiles.Select(p => p.DisplayName));   // the Claude group is unchanged
	}

	[Fact]
	public async Task ADaemonWithoutChatGptProfiles_ShowsNoChatGptGroup()
	{
		_client.CodexProfiles = null;

		await _profiles.AttachAsync(_client);

		Assert.False(_profiles.IsChatGptAvailable);
		Assert.Empty(_profiles.ChatGptProfiles);
	}

	[Fact]
	public async Task NewChatGptProfile_ByBrowser_OpensTheAddress_AndTheCompletionAddsTheProfile()
	{
		await _profiles.AttachAsync(_client);
		ChooseNext("browser");

		await _profiles.NewChatGptProfileAsync();

		Assert.Contains("profiles.codexLogin:browser:", _client.Calls);
		Assert.Equal(["https://auth.openai.com/oauth/authorize?fake=1"], _browser.Opened);
		var signIn = Assert.IsType<ChatGptSignInViewModel>(_profiles.SignIn);
		Assert.True(signIn.IsBrowser);
		Assert.False(signIn.ShowsAddress);
		Assert.False(_profiles.NewChatGptProfileCommand.CanExecute(null));

		_client.RaiseCodexLoginCompleted(new ProfileCodexLoginCompletedPayload("login-1", "codex_3", true, null, "n@example.com", "plus"));

		Assert.Null(_profiles.SignIn);
		Assert.Equal("Signed in to ChatGPT as n@example.com (ChatGPT Plus).", _profiles.StatusText);
		await _profiles.RefreshAsync();
		Assert.Contains(_profiles.ChatGptProfiles, p => p.Id == "codex_3");
	}

	[Fact]
	public async Task NewChatGptProfile_ByCode_ShowsTheCodeTheAddressAndTheNote_AndCopiesThem()
	{
		await _profiles.AttachAsync(_client);
		ChooseNext("code");

		await _profiles.NewChatGptProfileAsync();

		Assert.Contains("profiles.codexLogin:deviceCode:", _client.Calls);
		Assert.Empty(_browser.Opened);   // the code way does not open the browser by itself
		var signIn = _profiles.SignIn!;
		Assert.True(signIn.IsDeviceCode);
		Assert.Equal("ABCD-1234", signIn.UserCode);
		Assert.Equal("https://auth.openai.com/codex/device", signIn.Address);
		Assert.True(signIn.ShowsAddress);
		Assert.Contains("security settings", signIn.DeviceCodeNote);

		signIn.CopyCodeCommand.Execute(null);
		signIn.CopyAddressCommand.Execute(null);
		Assert.Equal(["ABCD-1234", "https://auth.openai.com/codex/device"], _clipboard.Written);
		Assert.Equal(Constants.Codex.Copied, signIn.CopyStatus);

		signIn.OpenAddressCommand.Execute(null);
		Assert.Equal(["https://auth.openai.com/codex/device"], _browser.Opened);
	}

	[Fact]
	public async Task AFailedSignIn_ShowsCodexsError_AndAddsNothing()
	{
		await _profiles.AttachAsync(_client);
		ChooseNext("browser");
		await _profiles.NewChatGptProfileAsync();

		_client.RaiseCodexLoginCompleted(new ProfileCodexLoginCompletedPayload("login-1", "codex_3", false, "Login was not completed", null, null));

		Assert.Null(_profiles.SignIn);
		Assert.Equal("The ChatGPT sign-in did not complete: Login was not completed", _profiles.StatusText);
		Assert.DoesNotContain(_profiles.ChatGptProfiles, p => p.Id == "codex_3");
	}

	[Fact]
	public async Task Cancel_EndsTheSignInOnTheDaemon()
	{
		await _profiles.AttachAsync(_client);
		ChooseNext("code");
		await _profiles.NewChatGptProfileAsync();

		await _profiles.CancelSignInAsync();

		Assert.Contains("profiles.codexLoginCancel:login-1", _client.Calls);
		Assert.Null(_profiles.SignIn);
	}

	[Fact]
	public async Task CancellingTheChoiceDialog_StartsNothing()
	{
		await _profiles.AttachAsync(_client);
		ChooseNext(null);

		await _profiles.NewChatGptProfileAsync();

		Assert.DoesNotContain(_client.Calls, c => c.StartsWith("profiles.codexLogin", StringComparison.Ordinal));
		Assert.Null(_profiles.SignIn);
	}

	[Fact]
	public async Task SignInAgain_SignsTheSameProfileIn()
	{
		await _profiles.AttachAsync(_client);
		ChooseNext("browser");

		await _profiles.SignInAgainAsync(_profiles.ChatGptProfiles.Single(p => p.Id == "codex_2"));

		Assert.Contains("profiles.codexLogin:browser:codex_2", _client.Calls);
		Assert.Equal("codex_2", _profiles.SignIn!.ProfileId);
	}

	[Fact]
	public async Task SignOut_SignsTheProfileOut_AndItStaysListed()
	{
		await _profiles.AttachAsync(_client);

		await _profiles.SignOutAsync(_profiles.ChatGptProfiles.Single(p => p.Id == "codex_1"));

		Assert.Contains("profiles.codexLogout:codex_1", _client.Calls);
		var row = _profiles.ChatGptProfiles.Single(p => p.Id == "codex_1");
		Assert.StartsWith("Signed out", row.StateText);
		Assert.Equal("ChatGPT · u@example.com is signed out.", _profiles.StatusText);
	}

	[Fact]
	public async Task Delete_AsksFirst_ThenRemovesTheProfile()
	{
		await _profiles.AttachAsync(_client);
		ChooseNext(null);   // the confirmation is declined
		await _profiles.DeleteChatGptAsync(_profiles.ChatGptProfiles.Single(p => p.Id == "codex_1"));
		Assert.DoesNotContain("profile.remove:codex_1", _client.Calls);

		ChooseNext("accept");
		_client.RemovedProfilePinnedSessions = 2;
		await _profiles.DeleteChatGptAsync(_profiles.ChatGptProfiles.Single(p => p.Id == "codex_1"));

		Assert.Contains("profile.remove:codex_1", _client.Calls);
		Assert.DoesNotContain(_profiles.ChatGptProfiles, p => p.Id == "codex_1");
		Assert.Equal("ChatGPT · u@example.com was deleted. 2 session(s) were pinned to it.", _profiles.StatusText);
		Assert.True(_presenter.Shown.Last().IsDestructive);
	}

	[Fact]
	public async Task CodexDefault_IsNeverDeleted()
	{
		await _profiles.AttachAsync(_client);
		ChooseNext("accept");

		await _profiles.DeleteChatGptAsync(_profiles.ChatGptProfiles.Single(p => p.IsCodexDefault));

		Assert.DoesNotContain(_client.Calls, c => c.StartsWith("profile.remove", StringComparison.Ordinal));
	}

	/// <summary>
	/// The row buttons' accessible names keep the id's underscore: WPF's button peer drops an unescaped underscore as an access-key marker, so
	/// <c>Sign out: ... (codex_1)</c> was read as <c>(codex1)</c> and TC.CX.41 could not find the button.
	/// </summary>
	[Fact]
	public void TheRowButtonNames_ReadByUiAutomation_KeepTheUnderscoresOfTheIdAndTheEmail()
	{
		var row = new ChatGptProfileRowViewModel(new CodexProfileDto("codex_1", "ChatGPT · u_x@example.com", "u_x@example.com", "plus", false, true, false, true));
		StaTestRunner.Run(() =>
		{
			foreach (var (bound, expected) in new[]
			{
				(row.SignOutName, "Sign out: ChatGPT · u_x@example.com (codex_1)"),
				(row.SignInAgainName, "Sign in again: ChatGPT · u_x@example.com (codex_1)"),
				(row.DeleteName, "Delete: ChatGPT · u_x@example.com (codex_1)"),
			})
			{
				var button = new System.Windows.Controls.Button { Content = "Sign out" };
				System.Windows.Automation.AutomationProperties.SetName(button, bound);
				Assert.Equal(expected, System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(button).GetName());
			}
		});
	}
}
