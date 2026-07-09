using System;
using Microsoft.Toolkit.Uwp.Notifications;
using Serilog;

namespace ClaudeMaximus.Services;

/// <summary>
/// Windows toast notification implementation using Microsoft.Toolkit.Uwp.Notifications (FR.16).
/// Works for unpackaged desktop apps on Windows 10 1809+ and Windows 11.
/// If the toast system cannot be initialised (e.g. running in a restricted context) all calls
/// are silently swallowed so the app continues functioning normally.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class WindowsNotificationService : INotificationService
{
	private static readonly ILogger _log = Log.ForContext<WindowsNotificationService>();
	private readonly IAppSettingsService _appSettings;
	private bool _activated;

	public WindowsNotificationService(IAppSettingsService appSettings)
	{
		_appSettings = appSettings;
	}

	public void RegisterActivationHandler(Action<string> onNodeIdActivated)
	{
		if (_activated)
			return;
		_activated = true;

		try
		{
			// Register static handler — fires on the ThreadPool when a toast is clicked.
			// Works while the app is running; no COM restart-activation is needed since
			// the app is always running when it shows toasts.
			ToastNotificationManagerCompat.OnActivated += args =>
			{
				try
				{
					var parsed = ToastArguments.Parse(args.Argument);
					if (parsed.TryGetValue(Constants.Notifications.ArgNodeId, out var nodeId)
					    && !string.IsNullOrEmpty(nodeId))
					{
						onNodeIdActivated(nodeId);
					}
				}
				catch (Exception ex)
				{
					_log.Warning(ex, "Toast activation handler error");
				}
			};
			_log.Information("Toast activation handler registered");
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "Could not register toast activation handler — click-to-activate disabled");
		}
	}

	public void ShowResult(string nodeId, string sessionName, string assistantText, string? note)
	{
		if (!_appSettings.Settings.NotificationsEnabled)
			return;

		if (string.IsNullOrWhiteSpace(assistantText))
			return;

		try
		{
			var body = assistantText.Length > Constants.Notifications.MaxBodyChars
				? assistantText[..Constants.Notifications.MaxBodyChars].TrimEnd() + "…"
				: assistantText;

			var builder = new ToastContentBuilder()
				.AddArgument(Constants.Notifications.ArgNodeId, nodeId)
				.AddText(sessionName)   // line 1 — title (bold)
				.AddText(body);          // line 2 — body

			if (!string.IsNullOrWhiteSpace(note))
				builder.AddAttributionText(note);

			builder.Show();
			_log.Debug("Toast shown for session {Name} node {NodeId}", sessionName, nodeId);
		}
		catch (Exception ex)
		{
			// Toast system unavailable (e.g. running in a session without notification support).
			// Log once and carry on — this must never crash the app.
			_log.Warning(ex, "Could not show toast notification for session {Name}", sessionName);
		}
	}
}
