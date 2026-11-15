using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>Package J2: who presents notifications. The registry, the toast fan-out and the completion sound of the host.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class NotificationPresenceTests : IDisposable
{
	private const string Toast = RemoteProtocol.Notifications.ToastChannel;
	private const string Sound = RemoteProtocol.Notifications.SoundChannel;
	private static readonly string[] Both = [Toast, Sound];

	private readonly string _dir = Path.Combine(Path.GetTempPath(), "presence-tests-" + Guid.NewGuid().ToString("N"));
	private readonly ClientPresenceRegistry _presence = new();
	private readonly RemoteEventBus _bus = new();
	private readonly RecordingHostNotifications _host = new();
	private readonly AppSettingsService _settings;
	private readonly NotificationFanOut _toasts;
	private readonly PresenceAwareSessionSoundService _sounds;
	private readonly List<RemoteNotification> _published = [];

	public NotificationPresenceTests()
	{
		Directory.CreateDirectory(_dir);
		_settings = new AppSettingsService(_dir);
		_bus.Published += n => { lock (_published) _published.Add(n); };
		_toasts = new NotificationFanOut(_host, _presence, _bus, _settings);
		_sounds = new PresenceAwareSessionSoundService(_host, _presence);
	}

	public void Dispose()
	{
		_bus.Dispose();
		Directory.Delete(_dir, recursive: true);
	}

	private List<RemoteNotification> Raised()
	{
		lock (_published)
			return _published.Where(n => n.Method == RemoteProtocol.Notifications.RaisedEvent).ToList();
	}

	// -- Registry -----------------------------------------------------------------------------------------------------------------

	[Fact]
	public void ARegistrationWithoutAnOpenStream_PresentsNothing()
	{
		_presence.Register("wpf", Both);

		Assert.False(_presence.IsPresented(Toast));
		Assert.False(_presence.IsPresented(Sound));
	}

	[Fact]
	public void WhileTheStreamIsOpen_TheRegisteredChannelsArePresented_AndNotAfterItCloses()
	{
		_presence.Register("wpf", [Toast]);

		using (_presence.OpenStream("wpf"))
		{
			Assert.True(_presence.IsPresented(Toast));
			Assert.False(_presence.IsPresented(Sound));
		}

		Assert.False(_presence.IsPresented(Toast));
	}

	[Fact]
	public void TheRegistrationSurvivesAReconnect_ItCountsAgainWhenTheNextStreamOpens()
	{
		_presence.Register("wpf", Both);
		_presence.OpenStream("wpf").Dispose();

		using var again = _presence.OpenStream("wpf");

		Assert.True(_presence.IsPresented(Toast));
	}

	[Fact]
	public void TwoStreamsOfOneClient_KeepItPresentedUntilTheLastCloses()
	{
		_presence.Register("wpf", [Toast]);
		var first  = _presence.OpenStream("wpf");
		var second = _presence.OpenStream("wpf");

		first.Dispose();
		Assert.True(_presence.IsPresented(Toast));
		second.Dispose();
		second.Dispose(); // a second dispose of the same handle changes nothing
		Assert.False(_presence.IsPresented(Toast));
	}

	[Fact]
	public void SeveralClients_AnyOneOfThemIsEnough()
	{
		_presence.Register("a", [Toast]);
		_presence.Register("b", [Toast]);
		using var a = _presence.OpenStream("a");
		var b = _presence.OpenStream("b");

		b.Dispose();
		Assert.True(_presence.IsPresented(Toast));
		a.Dispose();
		Assert.False(_presence.IsPresented(Toast));
	}

	[Fact]
	public void RegisteringNothing_Unregisters()
	{
		_presence.Register("wpf", Both);
		using var stream = _presence.OpenStream("wpf");

		_presence.Register("wpf", []);

		Assert.False(_presence.IsPresented(Toast));
		Assert.False(_presence.IsPresented(Sound));
	}

	[Fact]
	public void AStreamWithoutAClientId_IsNoClient()
	{
		_presence.Register("wpf", [Toast]);

		using var anonymous = _presence.OpenStream(null);
		using var blank = _presence.OpenStream(" ");

		Assert.False(_presence.IsPresented(Toast));
	}

	[Fact]
	public void Changed_IsRaisedWhenARegistrationOrAStreamChanges()
	{
		var raised = 0;
		_presence.Changed += () => raised++;

		_presence.Register("wpf", [Toast]);
		var stream = _presence.OpenStream("wpf");
		stream.Dispose();

		Assert.Equal(3, raised);
	}

	// -- Toast: silent host, resumes, no double ---------------------------------------------------------------------------------

	[Fact]
	public void NoClient_TheHostShowsTheToast_OnceAndTheEventIsPublishedBesideIt()
	{
		var shown = _toasts.ShowUserNotification("node-1", "Title", "Body");

		Assert.True(shown);
		Assert.Equal(1, _host.ToastCount);
		var raised = Assert.Single(Raised());
		Assert.Equal("node-1", raised.Params["nodeId"]!.GetValue<string>());
		Assert.Equal("Title", raised.Params["title"]!.GetValue<string>());
		Assert.Equal("Body", raised.Params["message"]!.GetValue<string>());
		Assert.Equal(RemoteProtocol.Notifications.UserNotificationKind, raised.Params["kind"]!.GetValue<string>());
		Assert.False(string.IsNullOrEmpty(raised.Params["notificationId"]!.GetValue<string>()));
		Assert.NotNull(raised.Params["raisedUtc"]);
	}

	[Fact]
	public void WhileAClientPresentsToasts_TheHostStaysSilent_AndTheEventIsPublishedOnce()
	{
		_presence.Register("wpf", [Toast]);
		using var stream = _presence.OpenStream("wpf");

		var shown = _toasts.ShowUserNotification("node-1", "Title", "Body");

		Assert.True(shown); // delivered on the desktop, by the client
		Assert.Equal(0, _host.ToastCount);
		Assert.Single(Raised());
	}

	[Fact]
	public void WhenTheLastRegisteredClientDisconnects_TheHostResumes()
	{
		_presence.Register("wpf", [Toast]);
		var stream = _presence.OpenStream("wpf");
		_toasts.ShowUserNotification("node-1", "silent", "x");

		stream.Dispose();
		_toasts.ShowUserNotification("node-1", "audible", "y");

		var toast = Assert.Single(_host.Toasts);
		Assert.Equal("audible", toast.Title);
		Assert.Equal(2, Raised().Count);
	}

	[Fact]
	public void ASoundOnlyClient_DoesNotSilenceTheToast()
	{
		_presence.Register("wpf", [Sound]);
		using var stream = _presence.OpenStream("wpf");

		_toasts.ShowUserNotification("node-1", "t", "m");

		Assert.Equal(1, _host.ToastCount);
	}

	[Fact]
	public void WithNotificationsDisabled_NothingIsShownOrPublished()
	{
		_settings.Settings.NotificationsEnabled = false;

		var shown = _toasts.ShowUserNotification("node-1", "t", "m");

		Assert.False(shown);
		Assert.Equal(0, _host.ToastCount);
		Assert.Empty(Raised());
	}

	[Fact]
	public void EveryNotificationGetsItsOwnId()
	{
		_toasts.ShowUserNotification("n", "t", "m");
		_toasts.ShowUserNotification("n", "t", "m");

		Assert.Equal(2, Raised().Select(r => r.Params["notificationId"]!.GetValue<string>()).Distinct().Count());
	}

	// -- Sound --------------------------------------------------------------------------------------------------------------------

	[Fact]
	public void TheCompletionSound_PlaysOnTheHost_UntilAClientTakesItOver_AndResumesAfterIt()
	{
		_sounds.PlayCompletionSound("n");
		Assert.Equal(1, _host.SoundCount);

		_presence.Register("wpf", [Sound]);
		var stream = _presence.OpenStream("wpf");
		_sounds.PlayCompletionSound("n");
		Assert.Equal(1, _host.SoundCount);

		stream.Dispose();
		_sounds.PlayCompletionSound("n");
		Assert.Equal(2, _host.SoundCount);
	}

	[Fact]
	public void AToastOnlyClient_DoesNotSilenceTheSound()
	{
		_presence.Register("wpf", [Toast]);
		using var stream = _presence.OpenStream("wpf");

		_sounds.PlayCompletionSound("n");

		Assert.Equal(1, _host.SoundCount);
	}

	[Fact]
	public void TheSettingsTestSound_AlwaysPlays()
	{
		_presence.Register("wpf", Both);
		using var stream = _presence.OpenStream("wpf");

		_sounds.PlayTestSound(@"C:\x.wav");

		Assert.Equal([@"C:\x.wav"], _host.TestSounds);
	}
}
