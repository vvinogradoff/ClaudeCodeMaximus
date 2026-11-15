using System.Diagnostics.CodeAnalysis;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Client;

/// <summary>Reads the package J3 part 2 notifications of <see cref="ITessynClient.NotificationReceived"/>; false when it is another method or does not fit.</summary>
public static class IntegrationsNotificationExtensions
{
	/// <summary><c>mcp.changed</c>: a server was added, changed, removed or authorized (reload <c>McpListAsync</c>).</summary>
	public static bool TryGetMcpChanged(this RemoteNotification notification, [NotNullWhen(true)] out McpChangedPayload? payload) =>
		notification.TryRead(RemoteProtocol.McpApi.ChangedEvent, out payload);

	/// <summary><c>catalog.changed</c>: the model catalog was refreshed (reload <c>CatalogGetAsync</c>).</summary>
	public static bool IsCatalogChanged(this RemoteNotification notification) =>
		notification.Method == RemoteProtocol.ModelsApi.CatalogChangedEvent;
}
