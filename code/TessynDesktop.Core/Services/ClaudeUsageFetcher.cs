using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <inheritdoc cref="IClaudeUsageFetcher" />
/// <remarks>Created by Claude</remarks>
public sealed class ClaudeUsageFetcher : IClaudeUsageFetcher
{
	private static readonly ILogger _log = Log.ForContext<ClaudeUsageFetcher>();

	private readonly HttpClient _httpClient;

	public ClaudeUsageFetcher()
	{
		_httpClient = new HttpClient();
		_httpClient.DefaultRequestHeaders.Add("anthropic-beta", Constants.Usage.BetaHeaderValue);
	}

	/// <inheritdoc />
	public async Task<UsageFetchResultModel> FetchAsync(string credentialsPath, CancellationToken cancellationToken)
	{
		var token = ClaudeUsageService.ReadAccessToken(credentialsPath);
		if (token == null)
			return new UsageFetchResultModel(UsageState.Unavailable, null);

		try
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, Constants.Usage.Endpoint);
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

			using var response = await _httpClient.SendAsync(request, cancellationToken);
			if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
				return new UsageFetchResultModel(UsageState.Unauthorized, null);

			if (!response.IsSuccessStatusCode)
			{
				_log.Debug("Usage fetch: HTTP {Code}", (int)response.StatusCode);
				return new UsageFetchResultModel(UsageState.Unavailable, null);
			}

			var usage = ClaudeUsageService.ParseUsage(await response.Content.ReadAsStringAsync(cancellationToken));
			return usage == null
				? new UsageFetchResultModel(UsageState.Unavailable, null)
				: new UsageFetchResultModel(UsageState.Ok, usage);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			_log.Debug(ex, "Usage fetch failed");
			return new UsageFetchResultModel(UsageState.Unavailable, null);
		}
	}
}
