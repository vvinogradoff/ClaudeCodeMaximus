using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;

namespace Tessyn.Web.E2E.Tests.Harness;

/// <summary>Stands in for the usage endpoint: a fixed reading.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FakeUsageFetcher : IClaudeUsageFetcher
{
	public const double FiveHour = 42;
	public const double SevenDay = 17;

	public Task<UsageFetchResultModel> FetchAsync(string credentialsPath, CancellationToken cancellationToken)
	{
		var now = DateTimeOffset.UtcNow;
		var data = new ClaudeUsageData(FiveHour, now.AddHours(3), "normal", SevenDay, now.AddDays(4), "normal");
		return Task.FromResult(new UsageFetchResultModel(UsageState.Ok, data));
	}
}
