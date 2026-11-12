using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// One-shot fetch of the Anthropic OAuth usage endpoint for a given credentials file. Unlike
/// <see cref="IClaudeUsageService"/> it keeps no state and has no notion of an active profile, so any number of
/// profiles can be read side by side.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IClaudeUsageFetcher
{
	/// <summary>Reads the access token from <paramref name="credentialsPath"/> and asks the endpoint. Never throws except for cancellation.</summary>
	Task<UsageFetchResultModel> FetchAsync(string credentialsPath, CancellationToken cancellationToken);
}
