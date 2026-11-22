using System;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// The plan usage of one engine's profiles (FR.33.1 "usage source", FR.33.10). <c>usage.get</c> asks the source that handles the profile id;
/// the Claude profiles stay with the existing reader.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IEngineUsageSource
{
	/// <summary>True for a profile id of this engine.</summary>
	bool Handles(string profileId);

	/// <summary>The profile's reading, fetched when it is older than the cache duration (FR.18.6); a failed read keeps the last reading.</summary>
	Task<UsageResult> GetAsync(string profileId, CancellationToken cancellationToken);

	/// <summary>Raised with the profile id when its reading changed (a notification from the engine, or a new read).</summary>
	event Action<string>? Changed;
}
