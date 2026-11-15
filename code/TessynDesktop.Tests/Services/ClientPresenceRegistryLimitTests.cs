using System;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>V7: the presence registry is bounded and forgets registrations nobody refreshed.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ClientPresenceRegistryLimitTests
{
	private sealed class ManualTime : TimeProvider
	{
		private DateTimeOffset _now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
		public override DateTimeOffset GetUtcNow() => _now;
		public void Advance(TimeSpan by) => _now += by;
	}

	private readonly ManualTime _time = new();
	private readonly ClientPresenceRegistry _sut;

	public ClientPresenceRegistryLimitTests() => _sut = new ClientPresenceRegistry(_time);

	[Fact]
	public void MoreThanTheMaximumOfClients_AreRefused_ButAKnownOneCanStillChangeItsChannels()
	{
		for (var i = 0; i < Constants.Composer.MaxPresenceClients; i++)
			_sut.Register("client-" + i, ["toast"]);

		Assert.Throws<InvalidOperationException>(() => _sut.Register("one-too-many", ["toast"]));
		_sut.Register("client-0", ["sound"]); // already known: allowed
		_sut.Register("one-too-many", []); // an unregister of an unknown id never needs room
	}

	[Fact]
	public void ARegistrationWithoutAStream_IsForgottenAfterTheTtl_WhichMakesRoom()
	{
		for (var i = 0; i < Constants.Composer.MaxPresenceClients; i++)
			_sut.Register("client-" + i, ["toast"]);

		_time.Advance(Constants.Composer.PresenceIdleTtl + TimeSpan.FromSeconds(1));
		_sut.Register("newcomer", ["toast"]);

		using var stream = _sut.OpenStream("client-0");
		Assert.False(_sut.IsPresented("toast")); // client-0 was forgotten with the others: it has to register again
	}

	[Fact]
	public void AClientWithAnOpenStream_IsNeverForgotten_HoweverOldItsRegistration()
	{
		_sut.Register("wpf", ["toast"]);
		using var stream = _sut.OpenStream("wpf");

		_time.Advance(TimeSpan.FromHours(3));
		_sut.Register("other", ["sound"]);

		Assert.True(_sut.IsPresented("toast"));
	}

	[Fact]
	public void ARegistrationRefreshedWithinTheTtl_Stays()
	{
		_sut.Register("wpf", ["toast"]);
		_time.Advance(Constants.Composer.PresenceIdleTtl - TimeSpan.FromSeconds(1));
		_sut.Register("wpf", ["toast"]); // refreshed
		_time.Advance(Constants.Composer.PresenceIdleTtl - TimeSpan.FromSeconds(1));
		_sut.Register("other", []);

		using var stream = _sut.OpenStream("wpf");
		Assert.True(_sut.IsPresented("toast"));
	}
}
