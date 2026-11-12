using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// Plan usage per profile for remote clients: each profile is fetched on demand and cached for five minutes, independent of
/// the desktop's active-profile poller (which is never touched). Publishes usage.changed when a fetch brings new data and
/// when the desktop poller updates.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IRemoteUsageService
{
	/// <summary>
	/// The usage of <paramref name="profileId"/> ("default" or a stored profile's id). Waits for a fetch for at most
	/// <c>Constants.Remote.Usage.FetchWait</c>; without data by then the state is "pending" and the fetch carries on.
	/// An unknown profile throws <see cref="TdcpInvalidParamsException"/>.
	/// </summary>
	Task<UsageResult> GetAsync(string profileId, CancellationToken cancellationToken);
}
