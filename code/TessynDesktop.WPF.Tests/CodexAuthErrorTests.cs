using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// FR.33.13: a ChatGPT auth error offers <b>Sign in again</b> (<c>profiles.codexLogin</c> with the profile's id), never Claude's re-authenticate, in
/// the composer's auth notice and in the status-bar profile flyout.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexAuthErrorTests
{
	private const string Node = "g";

	private readonly FakeTessynClient _client = new() { Catalog = CodexCatalogs.FixtureC() };
	private readonly FakeSystemUrlLauncher _browser = new();
	private readonly FakeDialogPresenter _presenter = new();

	public CodexAuthErrorTests()
	{
		_client.ServerInfo = new HelloResult(1, "test", "machine", "run", DateTimeOffset.UtcNow,
			[RemoteProtocol.ProfilesAdmin.Capability, RemoteProtocol.CodexProfilesAdmin.Capability], 1, 0);
		_client.Profiles = [new("default", "[Sys] me@example.com", true, "me@example.com", ProfileAuthState.Unknown, true), new("profile_1", "Work", false, null, ProfileAuthState.LoggedIn, false)];
		_client.CodexProfiles =
		[
			new("codex-default", "ChatGPT · me@example.com", "me@example.com", "plus", true, true, false, false),
			new("codex_1", CodexCatalogs.ChatGptName, "u@example.com", "plus", false, true, AuthError: true, false),
			new("codex_2", "ChatGPT · w@example.com", "w@example.com", "pro", false, true, false, false),
		];
	}

	private SessionPaneViewModel CreatePane() =>
		new(_client, new FakeTranscriptSurface(), new MarkdigEntryRenderer(new UrlPolicy()), new FakeUrlLauncher(), new ImmediateUiDispatcher(),
			new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance), NullLogger<SessionPaneViewModel>.Instance, Constants.Limits.MaxCachedTranscripts,
			systemLauncher: _browser);

	private async Task<SessionPaneViewModel> OpenedAsync(string profileId, string profileName, bool authError = true, string model = "gpt-x-sol")
	{
		var session = FakeSession.WithEntries(2, "G");
		session.Engine = model.StartsWith("gpt-", StringComparison.Ordinal) ? RemoteProtocol.Engines.Codex : RemoteProtocol.Engines.Claude;
		session.LastUsed = new LastUsedSettingsDto(model, "default", profileId, profileName);
		session.AuthError = authError;
		_client.Sessions[Node] = session;
		var pane = CreatePane();
		await pane.LoadCatalogAsync();
		await pane.OpenAsync(Node);
		return pane;
	}

	private ProfileViewModel NewFlyout()
	{
		var ui = new ImmediateUiDispatcher();
		return new ProfileViewModel(new DialogService(_presenter, ui), ui, NullLogger<ProfileViewModel>.Instance, _browser, new FakeClipboardWriter());
	}

	private void ChooseBrowserNext() => _presenter.Answer = dialog =>
	{
		dialog.ChooseCommand.Execute(dialog.Choices.Single(c => c.Id == Constants.Codex.BrowserChoiceId));
		return Task.CompletedTask;
	};

	// ---- the composer's auth notice ----

	[Fact]
	public async Task AChatGptPinWithAnAuthError_ShowsTheNotice_WithSignInAgain()
	{
		using var pane = await OpenedAsync("codex_1", CodexCatalogs.ChatGptName);

		Assert.True(pane.HasAuthError);
		Assert.Equal("codex_1", pane.ChatGptProfileId);
		Assert.Equal("Sign in again", pane.ReauthenticateLabel);
	}

	[Fact]
	public async Task SignInAgain_StartsTheChatGptSignInOfThatProfile_InTheBrowser_AndNeverClaudesReauthenticate()
	{
		using var pane = await OpenedAsync("codex_1", CodexCatalogs.ChatGptName);

		await pane.ReauthenticateCommand.ExecuteAsync(null);

		Assert.Contains("profiles.codexLogin:browser:codex_1", _client.Calls);
		Assert.DoesNotContain(_client.Calls, c => c.StartsWith("profile.authenticate", StringComparison.Ordinal));
		Assert.Equal(["https://auth.openai.com/oauth/authorize?fake=1"], _browser.Opened);
		Assert.Equal(Constants.Codex.BannerBrowserOpened, pane.AuthNotice);
	}

	[Fact]
	public async Task TheCompletionOfThatSignIn_IsReported_SuccessOrFailure()
	{
		using var pane = await OpenedAsync("codex_1", CodexCatalogs.ChatGptName);
		await pane.ReauthenticateCommand.ExecuteAsync(null);

		_client.RaiseCodexLoginCompleted(new ProfileCodexLoginCompletedPayload("other-login", "codex_9", false, "not ours", null, null));
		Assert.Equal(Constants.Codex.BannerBrowserOpened, pane.AuthNotice);   // another sign-in's end is not this one's

		_client.RaiseCodexLoginCompleted(new ProfileCodexLoginCompletedPayload("login-1", "codex_1", true, null, "u@example.com", "plus"));
		Assert.Equal("Signed in to ChatGPT as u@example.com. Send again to continue.", pane.AuthNotice);
	}

	[Fact]
	public async Task AFailedSignInAgain_SaysWhy()
	{
		using var pane = await OpenedAsync("codex_1", CodexCatalogs.ChatGptName);
		await pane.ReauthenticateCommand.ExecuteAsync(null);

		_client.RaiseCodexLoginCompleted(new ProfileCodexLoginCompletedPayload("login-1", "codex_1", false, "Login was not completed", null, null));

		Assert.Equal("The ChatGPT sign-in did not complete: Login was not completed", pane.AuthNotice);
	}

	[Fact]
	public async Task AChatGptPinTheCatalogDoesNotList_IsStillAChatGptProfile()
	{
		// The catalog lists only signed-in ChatGPT profiles; a profile with an auth error can be missing from it.
		using var pane = await OpenedAsync("codex_7", "ChatGPT · gone@example.com");

		Assert.Equal("codex_7", pane.ChatGptProfileId);
		await pane.ReauthenticateCommand.ExecuteAsync(null);
		Assert.Contains("profiles.codexLogin:browser:codex_7", _client.Calls);
	}

	[Fact]
	public async Task AClaudePin_KeepsReauthenticate()
	{
		using var pane = await OpenedAsync("profile_1", "Work", model: "claude-opus-4-8");

		Assert.Null(pane.ChatGptProfileId);
		Assert.Equal(Constants.ComposerTexts.Reauthenticate, pane.ReauthenticateLabel);
		await pane.ReauthenticateCommand.ExecuteAsync(null);
		Assert.Contains("profile.authenticate:profile_1", _client.Calls);
		Assert.DoesNotContain(_client.Calls, c => c.StartsWith("profiles.codexLogin", StringComparison.Ordinal));
	}

	[Fact]
	public async Task TheNoticesButton_ReadsSignInAgain_ForAChatGptProfile()
	{
		using var pane = await OpenedAsync("codex_1", CodexCatalogs.ChatGptName);

		StaTestRunner.Run(() =>
		{
			var notices = new Views.ComposerNotices { DataContext = pane };
			notices.Measure(new System.Windows.Size(600, 200));
			notices.Arrange(new System.Windows.Rect(0, 0, 600, 200));
			notices.UpdateLayout();

			var button = FindButton(notices);
			Assert.Equal("Sign in again", button.Content);
			Assert.Equal("Sign in again", System.Windows.Automation.AutomationProperties.GetName(button));
		});
	}

	private static System.Windows.Controls.Button FindButton(System.Windows.DependencyObject root)
	{
		for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
		{
			var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
			if (child is System.Windows.Controls.Button button)
				return button;
			if (TryFind(child) is { } found)
				return found;
		}

		throw new InvalidOperationException("No button");

		static System.Windows.Controls.Button? TryFind(System.Windows.DependencyObject node)
		{
			try
			{
				return FindButton(node);
			}
			catch (InvalidOperationException)
			{
				return null;
			}
		}
	}

	// ---- the status-bar profile flyout ----

	[Fact]
	public async Task TheFlyout_ShowsTheSessionsChatGptProfile_WithItsState_AndSignInAgainOnAnAuthError()
	{
		var flyout = NewFlyout();
		await flyout.AttachAsync(_client);

		flyout.ShowSession("codex_1", hasAuthError: true);

		var row = Assert.IsType<ChatGptProfileRowViewModel>(flyout.SessionChatGptProfile);
		Assert.Equal(CodexCatalogs.ChatGptName, row.DisplayName);
		Assert.Equal("ChatGPT Plus", row.PlanText);
		Assert.StartsWith(Constants.Codex.AuthErrorText, row.StateText);
		Assert.True(flyout.SessionNeedsSignIn);
		Assert.Equal(["[Sys] me@example.com", "Work"], flyout.Profiles.Select(p => p.DisplayName));   // the Claude list (with re-authenticate) does not hold it
	}

	[Fact]
	public async Task TheFlyoutsSignInAgain_StartsProfilesCodexLoginWithThatProfileId()
	{
		var flyout = NewFlyout();
		await flyout.AttachAsync(_client);
		flyout.ShowSession("codex_1", hasAuthError: true);
		ChooseBrowserNext();

		await flyout.SignInSessionProfileAgainCommand.ExecuteAsync(null);

		Assert.Contains("profiles.codexLogin:browser:codex_1", _client.Calls);
		Assert.DoesNotContain(_client.Calls, c => c.StartsWith("profile.authenticate", StringComparison.Ordinal));
		Assert.Equal("codex_1", flyout.SignIn!.ProfileId);
	}

	[Fact]
	public async Task TheFlyout_OffersNoSignInForAHealthyProfile_AndShowsNothingForAClaudeSession()
	{
		var flyout = NewFlyout();
		await flyout.AttachAsync(_client);

		flyout.ShowSession("codex_2", hasAuthError: false);
		Assert.True(flyout.HasSessionChatGptProfile);
		Assert.False(flyout.SessionNeedsSignIn);

		flyout.ShowSession("codex_2", hasAuthError: true);   // the session's own flag counts too
		Assert.True(flyout.SessionNeedsSignIn);

		flyout.ShowSession(null, hasAuthError: true);
		Assert.False(flyout.HasSessionChatGptProfile);
		Assert.False(flyout.SessionNeedsSignIn);
	}

	[Fact]
	public async Task TheProfileHost_FollowsTheOpenSessionsChatGptProfile()
	{
		var flyout = NewFlyout();
		await flyout.AttachAsync(_client);
		var ui = new ImmediateUiDispatcher();
		var connection = new ConnectionViewModel(new ScriptedLocator(), new FixedClientFactory(_client), ui, NullLogger<ConnectionViewModel>.Instance, TimeSpan.FromMilliseconds(5));
		var switcher = new ProfileSwitchViewModel(new DialogService(_presenter, ui), new TreeContext(), ui, NullLogger<ProfileSwitchViewModel>.Instance);
		using var host = new ProfileHost(connection, flyout, switcher);
		using var pane = await OpenedAsync("codex_1", CodexCatalogs.ChatGptName);

		host.Observe(pane);
		Assert.Equal("codex_1", flyout.SessionChatGptProfile?.Id);
		Assert.True(flyout.SessionNeedsSignIn);

		pane.SelectedModel = pane.Models.Single(m => m.Id == "claude-opus-4-8");   // a Claude model: the session's next turn uses a Claude profile
		Assert.Null(flyout.SessionChatGptProfile);
	}
}
