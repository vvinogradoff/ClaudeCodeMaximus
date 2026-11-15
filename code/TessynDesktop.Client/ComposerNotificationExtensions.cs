using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;

namespace TessynDesktop.Client;

/// <summary>Typed readers of the composer notifications of package J2 (<c>draft.changed</c>).</summary>
public static class ComposerNotificationExtensions
{
	/// <summary>A session's draft changed (any writer, this client included): compare <see cref="DraftChangedPayload.Revision"/> with the one you last read or wrote.</summary>
	public static bool TryGetDraftChanged(this RemoteNotification notification, [NotNullWhen(true)] out DraftChangedPayload? payload)
	{
		payload = null;
		if (notification.Method != RemoteProtocol.Composer.DraftChangedEvent)
			return false;

		try
		{
			payload = notification.Params.Deserialize<DraftChangedPayload>(TdcpJsonOptions.Default);
			return payload != null;
		}
		catch (JsonException)
		{
			return false;
		}
	}
}
