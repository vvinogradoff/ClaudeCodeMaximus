using System.Text.Json.Nodes;

namespace TessynDesktop.Models.Remote;

/// <summary>One notification on the remote event bus: a method name (see <see cref="Constants.Remote.Events"/>) and its JSON params.</summary>
/// <param name="Method">Notification name, e.g. "session.changed".</param>
/// <param name="Params">The payload, already serialized with the shared wire options.</param>
/// <param name="Seq">Position in the daemon's event log (monotonic per daemon run, starting at 1); 0 while not yet stamped by the <c>IEventLog</c>.</param>
/// <remarks>Created by Claude</remarks>
public sealed record RemoteNotification(string Method, JsonNode Params, long Seq = 0);
