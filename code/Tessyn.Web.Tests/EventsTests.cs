using System.Text.Json;
using Tessyn.Web.Tests.Support;

namespace Tessyn.Web.Tests;

/// <remarks>Created by Claude</remarks>
public sealed class EventsTests
{
	private static JsonElement Parse(SseFrame frame)
	{
		return JsonDocument.Parse(frame.Data!).RootElement;
	}

	[Fact]
	public async Task First_frame_is_the_current_link_status_offline_then_online()
	{
		using var factory = new TessynWebFactory();
		using var browser = await factory.CreateLoggedInBrowserAsync();
		await using var events = await SseClient.OpenAsync(browser, Constants.Paths.Events);

		var first = Parse(await events.NextDataAsync());
		Assert.Equal("link.status", first.GetProperty("method").GetString());
		Assert.False(first.GetProperty("params").GetProperty("online").GetBoolean());
		Assert.True(first.GetProperty("params").TryGetProperty("since", out _));

		await using var daemon = await FakeDaemon.ConnectAsync(factory);
		var online = await events.NextDataAsync();
		Assert.True(Parse(online).GetProperty("params").GetProperty("online").GetBoolean());
		Assert.NotNull(online.Id);
	}

	[Fact]
	public async Task First_frame_reports_online_when_a_daemon_is_already_connected()
	{
		using var factory = new TessynWebFactory();
		using var browser = await factory.CreateLoggedInBrowserAsync();
		await using var daemon = await FakeDaemon.ConnectAsync(factory);
		await using var events = await SseClient.OpenAsync(browser, Constants.Paths.Events);

		var first = Parse(await events.NextDataAsync());

		Assert.Equal("link.status", first.GetProperty("method").GetString());
		Assert.True(first.GetProperty("params").GetProperty("online").GetBoolean());
	}

	[Fact]
	public async Task Events_stream_has_the_streaming_headers()
	{
		using var factory = new TessynWebFactory();
		using var browser = await factory.CreateLoggedInBrowserAsync();
		await using var events = await SseClient.OpenAsync(browser, Constants.Paths.Events);

		var response = events.Response;

		Assert.Equal(Constants.ContentTypes.EventStream, response.Content.Headers.ContentType!.MediaType);
		Assert.Equal("no", response.Headers.GetValues(Constants.Headers.AccelBuffering).Single());
		Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
	}

	[Fact]
	public async Task Events_posted_to_the_link_reach_browsers_with_increasing_ids()
	{
		using var factory = new TessynWebFactory();
		using var browser = await factory.CreateLoggedInBrowserAsync();
		await using var daemon = await FakeDaemon.ConnectAsync(factory);
		await using var events = await SseClient.OpenAsync(browser, Constants.Paths.Events);
		await events.NextDataAsync();

		var post = await daemon.PostEventsAsync(
			"[{\"method\":\"session.changed\",\"params\":{\"state\":{\"id\":\"a\",\"running\":true}}},{\"method\":\"tree.changed\",\"params\":{\"version\":3}}]");
		var one = await events.NextDataAsync();
		var two = await events.NextDataAsync();

		Assert.Equal(System.Net.HttpStatusCode.NoContent, post.StatusCode);
		Assert.Equal("session.changed", Parse(one).GetProperty("method").GetString());
		Assert.True(Parse(one).GetProperty("params").GetProperty("state").GetProperty("running").GetBoolean());
		Assert.Equal("tree.changed", Parse(two).GetProperty("method").GetString());
		Assert.Equal(3, Parse(two).GetProperty("params").GetProperty("version").GetInt32());
		Assert.Equal(long.Parse(one.Id!) + 1, long.Parse(two.Id!));
	}

	[Fact]
	public async Task The_daemon_cannot_spoof_server_made_notifications()
	{
		using var factory = new TessynWebFactory();
		using var browser = await factory.CreateLoggedInBrowserAsync();
		await using var daemon = await FakeDaemon.ConnectAsync(factory);
		await using var events = await SseClient.OpenAsync(browser, Constants.Paths.Events);
		await events.NextDataAsync();

		await daemon.PostEventsAsync("[{\"method\":\"link.status\",\"params\":{\"online\":false}},{\"method\":\"resync\"},{\"method\":\"index.changed\",\"params\":{}}]");
		var next = await events.NextDataAsync();

		Assert.Equal("index.changed", Parse(next).GetProperty("method").GetString());
	}

	[Fact]
	public async Task Last_event_id_replays_only_newer_events()
	{
		using var factory = new TessynWebFactory();
		using var browser = await factory.CreateLoggedInBrowserAsync();
		await using var daemon = await FakeDaemon.ConnectAsync(factory);
		await using var live = await SseClient.OpenAsync(browser, Constants.Paths.Events);
		await live.NextDataAsync();
		await daemon.PostEventsAsync("[{\"method\":\"e1\"},{\"method\":\"e2\"},{\"method\":\"e3\"}]");
		var first = await live.NextDataAsync();
		await live.NextDataAsync();
		await live.NextDataAsync();

		await using var resumed = await SseClient.OpenAsync(browser, Constants.Paths.Events, r => r.Headers.Add(Constants.Headers.LastEventId, first.Id));
		var status = await resumed.NextDataAsync();
		var second = await resumed.NextDataAsync();
		var third = await resumed.NextDataAsync();

		Assert.Equal("link.status", Parse(status).GetProperty("method").GetString());
		Assert.Null(status.Id);
		Assert.Equal("e2", Parse(second).GetProperty("method").GetString());
		Assert.Equal("e3", Parse(third).GetProperty("method").GetString());
		Assert.Equal(long.Parse(first.Id!) + 1, long.Parse(second.Id!));
	}

	[Fact]
	public async Task Last_event_id_equal_to_the_latest_replays_nothing()
	{
		using var factory = new TessynWebFactory();
		using var browser = await factory.CreateLoggedInBrowserAsync();
		await using var daemon = await FakeDaemon.ConnectAsync(factory);
		await using var live = await SseClient.OpenAsync(browser, Constants.Paths.Events);
		await live.NextDataAsync();
		await daemon.PostEventsAsync("[{\"method\":\"e1\"}]");
		var last = await live.NextDataAsync();

		await using var resumed = await SseClient.OpenAsync(browser, Constants.Paths.Events, r => r.Headers.Add(Constants.Headers.LastEventId, last.Id));
		await resumed.NextDataAsync();
		await daemon.PostEventsAsync("[{\"method\":\"e2\"}]");
		var next = await resumed.NextDataAsync();

		Assert.Equal("e2", Parse(next).GetProperty("method").GetString());
	}

	[Fact]
	public async Task A_last_event_id_that_fell_out_of_the_buffer_gets_resync()
	{
		using var factory = new TessynWebFactory().Set("TessynWeb:Link:RingBufferEvents", "3");
		using var browser = await factory.CreateLoggedInBrowserAsync();
		await using var daemon = await FakeDaemon.ConnectAsync(factory);
		await daemon.PostEventsAsync("[{\"method\":\"e1\"},{\"method\":\"e2\"},{\"method\":\"e3\"},{\"method\":\"e4\"},{\"method\":\"e5\"},{\"method\":\"e6\"}]");

		await using var resumed = await SseClient.OpenAsync(browser, Constants.Paths.Events, r => r.Headers.Add(Constants.Headers.LastEventId, "1"));
		var status = await resumed.NextDataAsync();
		var resync = await resumed.NextDataAsync();

		Assert.Equal("link.status", Parse(status).GetProperty("method").GetString());
		Assert.Equal("resync", Parse(resync).GetProperty("method").GetString());
		Assert.NotNull(resync.Id);
	}

	[Fact]
	public async Task A_last_event_id_from_the_future_gets_resync()
	{
		using var factory = new TessynWebFactory();
		using var browser = await factory.CreateLoggedInBrowserAsync();

		await using var resumed = await SseClient.OpenAsync(browser, Constants.Paths.Events, r => r.Headers.Add(Constants.Headers.LastEventId, "99999"));
		await resumed.NextDataAsync();
		var resync = await resumed.NextDataAsync();

		Assert.Equal("resync", Parse(resync).GetProperty("method").GetString());
	}

	[Fact]
	public async Task Events_stream_sends_pings_when_idle()
	{
		using var factory = new TessynWebFactory().Set("TessynWeb:Link:PingSeconds", "0.2");
		using var browser = await factory.CreateLoggedInBrowserAsync();
		await using var events = await SseClient.OpenAsync(browser, Constants.Paths.Events);
		await events.NextDataAsync();

		var frame = await events.NextAsync();

		Assert.True(frame.IsPing);
	}
}
