using System;
using System.Linq;
using System.Threading.Tasks;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services.ElevatedRuns;

/// <summary>
/// The requester is named everywhere by the Short Node Id exactly as <c>list_sessions</c> computes it (the shortest unique prefix, at least 8 characters;
/// <see cref="SessionListIndex.GetShortId"/>), not by a fixed 8-character prefix: two sessions whose ids share a longer prefix must stay distinguishable (FR.23.31, tdcp-spec 10.A13).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ElevatedRunShortIdTests : IDisposable
{
	private readonly ElevatedRunTestEnv _env = new();

	public void Dispose() => _env.Dispose();

	public ElevatedRunShortIdTests()
	{
		// Two sessions whose ids agree in the first 11 characters: the short ids must be 12 characters long.
		_env.Lead.NodeId = "abcdef0123-lead-0000";
		_env.Wrk.NodeId = "abcdef0123-wrk0-0000";
		_env.Wrk.SupervisorNodeId = _env.Lead.NodeId;
	}

	private string ListSessionsShortId(string nodeId) => new SessionListIndex(_env.Registry.SnapshotSessionsForListing()).GetShortId(nodeId);

	[Fact]
	public async Task TheRequesterIsNamedByTheShortIdOfListSessions_InTheItem_TheNotificationAndTheWire()
	{
		var run = await _env.RequestAsync(_env.Lead);

		var expected = ListSessionsShortId(_env.Lead.NodeId);
		Assert.Equal("abcdef0123-l", expected);                                  // longer than a fixed 8-character prefix, which would not tell the two apart
		Assert.Equal(expected, _env.Service.ShortIdOf(_env.Lead.NodeId));
		Assert.Equal(expected, run.ToDto(_env.Service.GetLogPath(run.Id), relay: false, _env.Service.ShortIdOf(run.RequesterNodeId)).Requester.ShortId);

		var item = _env.Attention.GetItems(_env.Lead.NodeId).Single();
		Assert.Contains($"Requested by: Lead ({expected})", item.Body);
		Assert.Contains($"Lead ({expected}) asks to run a command as administrator", _env.Channels.Sent.Single().Message);
	}

	[Fact]
	public async Task AWorkersRequest_NamesTheWorkerByItsOwnShortId_OnTheSupervisorsMirroredItem()
	{
		var run = await _env.RequestAsync(_env.Wrk);

		var worker = ListSessionsShortId(_env.Wrk.NodeId);
		Assert.Equal("abcdef0123-w", worker);
		var mirrored = _env.Attention.GetItems(_env.Lead.NodeId).Single();
		Assert.Contains(worker, mirrored.Body);
		Assert.Equal(worker, _env.Service.ShortIdOf(run.RequesterNodeId));
	}

	[Fact]
	public void ASessionThatLeftTheTree_GetsTheMinimumLengthPrefix()
	{
		Assert.Equal("12345678", _env.Service.ShortIdOf("1234567890-gone"));
		Assert.Equal("short", _env.Service.ShortIdOf("short"));
	}
}
