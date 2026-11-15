using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>What <c>telegram.status</c> reads from the bridge (package J3 part 2): whether it is running and the last poll failure, never with the token.</summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class TelegramBridgeServiceTests
{
	private const string BotToken = "123456789:AAEtokenInsideTheExceptionText_0123456789";

	[Theory]
	[InlineData(401, "Telegram rejected the bot token")]
	[InlineData(404, "Telegram rejected the bot token")]
	[InlineData(429, "Telegram answered with error 429")]
	public void DescribeFailure_NamesATelegramError_WithoutItsDescription(int code, string expected)
	{
		var text = TelegramBridgeService.DescribeFailure(new TelegramApiException(code, $"Unauthorized, bot{BotToken}"));

		Assert.Equal(expected, text);
	}

	[Fact]
	public void DescribeFailure_NeverRepeatsTheExceptionText_WhateverTheType()
	{
		Exception[] failures =
		[
			new HttpRequestException($"Name or service not known (api.telegram.org:443) bot{BotToken}"),
			new TaskCanceledException($"timeout bot{BotToken}"),
			new InvalidOperationException($"bot{BotToken}"),
		];

		foreach (var failure in failures)
			Assert.DoesNotContain("123456789", TelegramBridgeService.DescribeFailure(failure));

		Assert.Equal("Could not reach Telegram", TelegramBridgeService.DescribeFailure(failures[0]));
		Assert.Equal("The request to Telegram timed out", TelegramBridgeService.DescribeFailure(failures[1]));
		Assert.Equal("The Telegram poll failed", TelegramBridgeService.DescribeFailure(failures[2]));
	}

	[Fact]
	public async Task IsRunningAndLastError_FollowTheBridge()
	{
		_client.PollFailure = () => new TelegramApiException(401, $"Unauthorized bot{BotToken}");
		var service = BuildService();
		Assert.False(((ITelegramBridgeService)service).IsRunning);
		Assert.Null(((ITelegramBridgeService)service).LastError);

		service.Start();
		await WaitUntilAsync(() => service.LastError != null);

		Assert.True(service.IsRunning);
		Assert.Equal("Telegram rejected the bot token", service.LastError);

		service.Stop();
		Assert.False(service.IsRunning);
		Assert.Null(service.LastError);                                            // a stopped bridge has no error to show
	}

	[Fact]
	public async Task ASuccessfulPoll_ClearsTheLastError()
	{
		var failures = 1;
		_client.UpdateBatches.Enqueue([]);                                         // the poll after the failure answers at once (a real one returns within 30 s)
		_client.PollFailure = () => Interlocked.Decrement(ref failures) >= 0 ? new HttpRequestException("down") : null;
		var service = BuildService();
		service.PollRetryDelay = TimeSpan.FromMilliseconds(400);   // long enough to see the error before the retry clears it
		service.Start();
		await WaitUntilAsync(() => service.LastError != null);

		// the loop retries after its pause; the next poll succeeds (and then waits like a real long poll)
		await WaitUntilAsync(() => service.LastError == null);

		Assert.True(service.IsRunning);
	}

	private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan? timeout = null)
	{
		var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
		while (!condition())
		{
			if (DateTime.UtcNow > deadline)
				throw new TimeoutException("The condition did not become true");
			await Task.Delay(20);
		}
	}
}
