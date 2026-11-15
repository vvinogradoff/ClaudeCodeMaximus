using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Tests.Fakes;

namespace TessynDesktop.WPF.Tests;

/// <summary>The package J2 part of <see cref="FakeTessynClient"/> behaves like the daemon's draft and turn options (WPF packages rely on it).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FakeTessynClientComposerTests
{
	private readonly FakeTessynClient _client = new();

	[Fact]
	public async Task Draft_StaleBaseRevision_ThrowsDraftChanged_LikeTheDaemon()
	{
		await _client.DraftSetAsync("n", "first");

		var error = await Assert.ThrowsAsync<TessynRpcException>(() => _client.DraftSetAsync("n", "second", RemoteProtocol.Composer.EmptyDraftRevision));

		Assert.Equal("Draft changed", error.Message);
		Assert.Equal("first", _client.DraftTexts["n"]);
	}

	[Fact]
	public async Task Draft_EmptyText_Deletes_AndTheCurrentRevisionIsAccepted()
	{
		var first = await _client.DraftSetAsync("n", "first");

		var second = await _client.DraftSetAsync("n", "second", first.Revision);
		var cleared = await _client.DraftSetAsync("n", string.Empty, second.Revision);

		Assert.Equal(RemoteProtocol.Composer.EmptyDraftRevision, cleared.Revision);
		Assert.False(_client.DraftTexts.ContainsKey("n"));
	}

	[Fact]
	public async Task TurnSend_RecordsTheOneShotToggles()
	{
		await _client.TurnSendAsync("n", "text", newBranch: true, autoCompact: true);

		var call = Assert.Single(_client.TurnSends);
		Assert.True(call.NewBranch);
		Assert.True(call.AutoCompact);
	}
}
