using System;
using System.Threading.Tasks;

namespace TessynDesktop.Services;

/// <summary>
/// Fans a turn-completion result out to both the desktop toast channel and the Telegram Bridge
/// (FR.21.5), gated independently: <see cref="WindowsNotificationService"/> checks
/// <c>NotificationsEnabled</c> internally; the Telegram side checks <c>TelegramEnabled</c> inside
/// <see cref="ITelegramBridgeService.NotifyAsync"/>. Callers (e.g. <c>SchedulerService</c>) see no
/// change — they still call the single <see cref="INotificationService.ShowResult"/> method.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class CompositeNotificationService : INotificationService
{
	private readonly WindowsNotificationService _windows;
	private readonly Lazy<ITelegramBridgeService> _telegramBridge;

	public CompositeNotificationService(WindowsNotificationService windows, Lazy<ITelegramBridgeService> telegramBridge)
	{
		_windows = windows;
		_telegramBridge = telegramBridge;
	}

	public void RegisterActivationHandler(Action<string> onNodeIdActivated) =>
		_windows.RegisterActivationHandler(onNodeIdActivated);

	public void ShowResult(string nodeId, string sessionName, string assistantText, string? note)
	{
		_windows.ShowResult(nodeId, sessionName, assistantText, note);

		if (string.IsNullOrWhiteSpace(assistantText))
			return;

		// FR.21.9's Universal I/O Mirror already streamed assistantText into the topic as the turn
		// produced it (SessionTurnService.RunTurnAsync appends every assistant block it receives),
		// so this is a short completion ping, not a repeat of the content.
		var suffix = string.IsNullOrWhiteSpace(note) ? string.Empty : $" [{note}]";
		_ = _telegramBridge.Value.NotifyAsync(nodeId, $"✅ finished{suffix} — see topic above for output.");
	}
}
