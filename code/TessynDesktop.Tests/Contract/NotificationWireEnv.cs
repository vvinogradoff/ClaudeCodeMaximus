using System;
using System.IO;
using TessynDesktop.Services;
using TessynDesktop.Tests.Services;

namespace TessynDesktop.Tests.Contract;

/// <summary>
/// Notification presence over a real loopback <see cref="TdcpServer"/> (package J2): the real presence registry, toast fan-out and sound wrapper
/// over a recorded host, the real <c>client.hello</c> / <c>client.register</c> module, and a short stream keep-alive so a dropped connection is
/// noticed within a fraction of a second. Disposing deletes the temp dir.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class NotificationWireEnv : IDisposable
{
	private readonly string _dir = Path.Combine(Path.GetTempPath(), "notification-wire-tests-" + Guid.NewGuid().ToString("N"));

	public ClientPresenceRegistry Presence { get; } = new();
	public RemoteEventBus Bus { get; } = new();
	public RecordingHostNotifications Host { get; } = new();
	public NotificationFanOut Toasts { get; }
	public PresenceAwareSessionSoundService Sounds { get; }
	public TdcpDispatcher Dispatcher { get; }
	public TdcpTestHost Server { get; }

	public NotificationWireEnv(bool withPresence = true)
	{
		Directory.CreateDirectory(_dir);
		var settings = new AppSettingsService(_dir);
		Toasts = new NotificationFanOut(Host, Presence, Bus, settings);
		Sounds = new PresenceAwareSessionSoundService(Host, Presence);

		var runInfo  = new DaemonRunInfo();
		var eventLog = new EventLog(Bus);
		Dispatcher = new TdcpDispatcher([new ClientRpcModule(runInfo: runInfo, eventLog: eventLog, presence: withPresence ? Presence : null)]);
		Server = new TdcpTestHost(Dispatcher, bus: Bus, runInfo: runInfo, presence: Presence, keepAlive: TimeSpan.FromMilliseconds(100));
	}

	public void Dispose()
	{
		Server.Dispose();
		Bus.Dispose();
		Directory.Delete(_dir, recursive: true);
	}
}
