using System;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Contract;

/// <summary>The local <c>GET /tdcp</c> SSE stream: ids, bus parity, legacy frames, Last-Event-ID resume and resync (P1.A, P1.B).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SseStreamTests : IDisposable
{
	private readonly TdcpTestHost _host = new(new TdcpDispatcher([new ClientRpcModule()]));

	public void Dispose() => _host.Dispose();

	private string RunQuery => "?runId=" + _host.RunInfo.RunId;

	[Fact]
	public async Task Bus_notifications_arrive_with_increasing_ids_and_the_same_payload()
	{
		using var sse = await _host.OpenStreamAsync();

		_host.Publish(2);

		var first = await sse.NextAsync();
		var second = await sse.NextAsync();
		Assert.Equal(1, first.Id);
		Assert.Equal(2, second.Id);
		Assert.Equal("session.changed", first.Method);
		Assert.Equal("n0", first.Params!["name"]!.GetValue<string>());
		Assert.Equal("n1", second.Params!["name"]!.GetValue<string>());
		Assert.Equal("2.0", first.Notification["jsonrpc"]!.GetValue<string>());
	}

	[Fact]
	public async Task Legacy_frames_are_absent_unless_requested()
	{
		using var sse = await _host.OpenStreamAsync();

		_host.Client.RaiseTreeChanged(new TreeChangedEventArgs { Kind = TreeChangeKind.Reloaded });
		_host.Client.RaiseScheduleChanged();

		Assert.True(await sse.StaysQuietAsync(TimeSpan.FromMilliseconds(400)));
	}

	[Fact]
	public async Task Legacy_frames_arrive_without_an_id_on_legacy_1()
	{
		using var sse = await _host.OpenStreamAsync("?legacy=1");

		_host.Client.RaiseTreeChanged(new TreeChangedEventArgs { Kind = TreeChangeKind.Reloaded });
		_host.Publish(1);

		var legacy = await sse.NextAsync();
		var bus = await sse.NextAsync();
		Assert.Equal("tree.changed", legacy.Method);
		Assert.Equal(0, legacy.Id);
		Assert.Equal("session.changed", bus.Method);
		Assert.Equal(1, bus.Id);
	}

	[Fact]
	public async Task Last_Event_ID_replays_the_events_after_it_and_then_continues_live()
	{
		_host.Publish(5);
		using var sse = await _host.OpenStreamAsync(RunQuery, lastEventId: "3");

		var four = await sse.NextAsync();
		var five = await sse.NextAsync();
		_host.Publish(1, first: 5);
		var six = await sse.NextAsync();

		Assert.Equal([4L, 5L, 6L], new[] { four.Id, five.Id, six.Id });
		Assert.Equal("n3", four.Params!["name"]!.GetValue<string>());
		Assert.Equal("n5", six.Params!["name"]!.GetValue<string>());
	}

	[Fact]
	public async Task A_resume_point_at_the_head_replays_nothing()
	{
		_host.Publish(2);
		using var sse = await _host.OpenStreamAsync(RunQuery, lastEventId: "2");

		Assert.True(await sse.StaysQuietAsync(TimeSpan.FromMilliseconds(300)));
		_host.Publish(1, first: 2);
		Assert.Equal(3, (await sse.NextAsync()).Id);
	}

	[Fact]
	public async Task A_gap_in_the_ring_answers_with_a_resync_frame()
	{
		using var small = new TdcpTestHost(new TdcpDispatcher([new ClientRpcModule()]), logCapacity: 3);
		small.Publish(10);

		using var sse = await small.OpenStreamAsync("?runId=" + small.RunInfo.RunId, lastEventId: "2");

		var resync = await sse.NextAsync();
		Assert.Equal("resync", resync.Method);
		Assert.Equal("gap", resync.Params!["reason"]!.GetValue<string>());
		Assert.Equal(10, resync.Params["eventSeq"]!.GetValue<long>());
		Assert.Equal(10, resync.Id);

		small.Publish(1, first: 10);
		Assert.Equal(11, (await sse.NextAsync()).Id);
	}

	[Fact]
	public async Task A_resume_point_ahead_of_the_head_answers_with_a_resync_frame()
	{
		_host.Publish(2);
		using var sse = await _host.OpenStreamAsync(RunQuery, lastEventId: "500");

		var resync = await sse.NextAsync();
		Assert.Equal("resync", resync.Method);
		Assert.Equal("unknown-seq", resync.Params!["reason"]!.GetValue<string>());
	}

	[Fact]
	public async Task A_run_id_of_another_run_answers_with_a_resync_frame()
	{
		_host.Publish(2);
		using var sse = await _host.OpenStreamAsync("?runId=not-this-run", lastEventId: "1");

		var resync = await sse.NextAsync();
		Assert.Equal("resync", resync.Method);
		Assert.Equal("unknown-seq", resync.Params!["reason"]!.GetValue<string>());
	}

	[Fact]
	public async Task The_run_id_of_this_run_allows_the_replay()
	{
		_host.Publish(2);
		using var sse = await _host.OpenStreamAsync("?runId=" + _host.RunInfo.RunId, lastEventId: "1");

		Assert.Equal(2, (await sse.NextAsync()).Id);
	}

	[Fact]
	public async Task A_resume_without_a_run_id_answers_with_a_resync_frame_instead_of_replaying()
	{
		_host.Publish(2);
		using var sse = await _host.OpenStreamAsync(lastEventId: "1");

		var resync = await sse.NextAsync();
		Assert.Equal("resync", resync.Method);
		Assert.Equal("unknown-seq", resync.Params!["reason"]!.GetValue<string>());
		Assert.Equal(2, resync.Params["eventSeq"]!.GetValue<long>());
	}

	[Fact]
	public async Task Events_published_right_after_a_fresh_connect_are_never_lost()
	{
		// Regression for the connect window (A1): the stream counts as connected as soon as it is subscribed.
		for (var i = 0; i < 15; i++)
		{
			using var sse = await _host.OpenStreamAsync();
			_host.Publish(1, first: i);

			var frame = await sse.NextAsync();
			Assert.Equal(i + 1, frame.Id);
			Assert.Equal("n" + i, frame.Params!["name"]!.GetValue<string>());
		}
	}

	[Fact]
	public async Task Two_streams_see_the_same_ids()
	{
		using var a = await _host.OpenStreamAsync();
		using var b = await _host.OpenStreamAsync();

		_host.Publish(1);

		Assert.Equal((await a.NextAsync()).Id, (await b.NextAsync()).Id);
	}
}
