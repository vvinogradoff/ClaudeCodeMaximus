using System.Net;
using Tessyn.Web.Tests.Support;

namespace Tessyn.Web.Tests;

/// <remarks>Created by Claude</remarks>
public sealed class LinkEndpointTests
{
	[Fact]
	public async Task Link_stream_with_a_bad_token_is_401()
	{
		using var factory = new TessynWebFactory();

		await using var daemon = await FakeDaemon.ConnectAsync(factory, token: "wrong-token");

		Assert.Equal(HttpStatusCode.Unauthorized, daemon.Stream.Response.StatusCode);
	}

	[Fact]
	public async Task Link_stream_without_a_token_is_401()
	{
		using var factory = new TessynWebFactory();
		using var http = factory.CreateDaemonHttp();

		var response = await http.GetAsync(Constants.Paths.LinkStream, HttpCompletionOption.ResponseHeadersRead);

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	[Fact]
	public async Task Link_reply_and_events_with_a_bad_token_are_401()
	{
		using var factory = new TessynWebFactory();
		await using var daemon = await FakeDaemon.ConnectAsync(factory);

		var reply = await daemon.PostAsync(Constants.Paths.LinkReply, "[]", token: "nope");
		var events = await daemon.PostAsync(Constants.Paths.LinkEvents, "{\"events\":[]}", token: "nope");

		Assert.Equal(HttpStatusCode.Unauthorized, reply.StatusCode);
		Assert.Equal(HttpStatusCode.Unauthorized, events.StatusCode);
	}

	[Fact]
	public async Task Link_reply_and_events_reject_malformed_bodies()
	{
		using var factory = new TessynWebFactory();
		await using var daemon = await FakeDaemon.ConnectAsync(factory);

		var notArray = await daemon.PostAsync(Constants.Paths.LinkReply, "{}");
		var garbage = await daemon.PostAsync(Constants.Paths.LinkEvents, "{oops");

		Assert.Equal(HttpStatusCode.BadRequest, notArray.StatusCode);
		Assert.Equal(HttpStatusCode.BadRequest, garbage.StatusCode);
	}

	[Fact]
	public async Task Link_stream_is_an_unbuffered_event_stream_that_sends_pings()
	{
		using var factory = new TessynWebFactory().Set("TessynWeb:Link:PingSeconds", "0.2");
		await using var daemon = await FakeDaemon.ConnectAsync(factory);

		var response = daemon.Stream.Response;
		var frame = await daemon.Stream.NextAsync();

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal(Constants.ContentTypes.EventStream, response.Content.Headers.ContentType!.MediaType);
		Assert.Equal("no", response.Headers.GetValues(Constants.Headers.AccelBuffering).Single());
		Assert.True(frame.IsPing);
	}

	[Fact]
	public async Task Link_stream_rejects_an_unknown_protocol_version()
	{
		using var factory = new TessynWebFactory();
		using var http = factory.CreateDaemonHttp();
		var request = new HttpRequestMessage(HttpMethod.Get, Constants.Paths.LinkStream);
		request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TessynWebFactory.LinkToken);
		request.Headers.Add(Constants.Headers.Protocol, "9");

		var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
	}
}
