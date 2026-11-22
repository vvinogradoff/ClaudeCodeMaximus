using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using TessynDesktop.Codex;

namespace TessynDesktop.Tests.Codex.Fakes;

/// <summary>A <see cref="CodexAppServerHost"/> over the fake launcher and a manual clock, recording every notification it raises.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class CodexHostFixture : IAsyncDisposable
{
	public const string StoredHome = @"C:\Users\u\AppData\Roaming\TessynDesktop\codex-profiles\codex_1";

	public CodexHostFixture(string profileId = "codex_1", string? codexHome = StoredHome, Action<FakeCodexAppServer>? configure = null)
	{
		Launcher = new FakeCodexProcessLauncher { Configure = configure };
		Host     = new CodexAppServerHost(new CodexHostOptions
		{
			ExecutablePath   = @"C:\Codex\codex-x86_64-pc-windows-msvc.exe",
			ProfileId        = profileId,
			CodexHome        = codexHome,
			WorkingDirectory = codexHome ?? @"C:\Users\u\.codex",
			ClientVersion    = "1.2.3",
		}, Launcher, Clock);
		Host.NotificationReceived += n => Notifications.Enqueue(n);
		Host.Crashed              += c => Crashes.Enqueue(c);
	}

	public ManualTimerTimeProvider             Clock         { get; } = new();
	public FakeCodexProcessLauncher            Launcher      { get; }
	public CodexAppServerHost                  Host          { get; }
	public ConcurrentQueue<CodexNotification>  Notifications { get; } = new();
	public ConcurrentQueue<CodexHostCrashInfo> Crashes       { get; } = new();

	public FakeCodexAppServer Server => Launcher.Latest;

	public FakeCodexScripts Scripts => Launcher.Scripts;

	/// <summary>Waits until a notification matching <paramref name="predicate"/> was raised by the host.</summary>
	public async Task<CodexNotification> WaitForNotificationAsync(Func<CodexNotification, bool> predicate, TimeSpan? timeout = null)
	{
		var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
		while (DateTime.UtcNow < deadline)
		{
			var match = Notifications.FirstOrDefault(predicate);
			if (match is not null)
				return match;
			await Task.Delay(10);
		}
		throw new TimeoutException("The host raised no matching notification.");
	}

	public async ValueTask DisposeAsync() => await Host.DisposeAsync();
}
