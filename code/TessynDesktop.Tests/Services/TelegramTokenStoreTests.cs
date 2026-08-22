using System;
using System.IO;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <remarks>Created by Claude</remarks>
public sealed class TelegramTokenStoreTests : IDisposable
{
	private readonly string _tempDir =
		Path.Combine(Path.GetTempPath(), "TessynTelegramTokenStoreTests_" + Guid.NewGuid().ToString("N"));
	private readonly TelegramTokenStore _store;

	public TelegramTokenStoreTests()
	{
		_store = new TelegramTokenStore(Path.Combine(_tempDir, "telegram-token.dat"));
	}

	public void Dispose()
	{
		try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort cleanup */ }
	}

	[Fact]
	public void Load_NoStoredToken_ReturnsNull()
	{
		Assert.Null(_store.Load());
	}

	[Fact]
	public void SaveThenLoad_RoundTrips()
	{
		_store.Save("123456:ABC-DEF-bot-token");

		Assert.Equal("123456:ABC-DEF-bot-token", _store.Load());
	}

	[Fact]
	public void Save_Overwrites_ExistingToken()
	{
		_store.Save("old-token");
		_store.Save("new-token");

		Assert.Equal("new-token", _store.Load());
	}

	[Fact]
	public void Delete_RemovesStoredToken_SubsequentLoadReturnsNull()
	{
		_store.Save("some-token");
		_store.Delete();

		Assert.Null(_store.Load());
	}

	[Fact]
	public void Delete_NoStoredToken_DoesNotThrow()
	{
		_store.Delete();
	}
}
