using System;
using System.IO;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <remarks>Created by Claude</remarks>
public sealed class McpTokenStoreTests : IDisposable
{
	private readonly string _tempDir =
		Path.Combine(Path.GetTempPath(), "TessynMcpTokenStoreTests_" + Guid.NewGuid().ToString("N"));
	private readonly McpTokenStore _store;

	public McpTokenStoreTests()
	{
		_store = new McpTokenStore(_tempDir);
	}

	public void Dispose()
	{
		try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort cleanup */ }
	}

	[Fact]
	public void Load_NoStoredRecord_ReturnsNull()
	{
		Assert.Null(_store.Load("never-saved"));
	}

	[Fact]
	public void SaveThenLoad_RoundTripsAllFields()
	{
		var record = new McpTokenRecord
		{
			AccessToken  = "at_12345",
			RefreshToken = "rt_67890",
			TokenType    = "Bearer",
			ExpiresAtUtc = new DateTimeOffset(2026, 8, 12, 18, 0, 0, TimeSpan.Zero),
		};

		_store.Save("server-1", record);
		var loaded = _store.Load("server-1");

		Assert.NotNull(loaded);
		Assert.Equal(record.AccessToken, loaded!.AccessToken);
		Assert.Equal(record.RefreshToken, loaded.RefreshToken);
		Assert.Equal(record.TokenType, loaded.TokenType);
		Assert.Equal(record.ExpiresAtUtc, loaded.ExpiresAtUtc);
	}

	[Fact]
	public void SaveThenLoad_NullRefreshTokenAndExpiry_RoundTrips()
	{
		var record = new McpTokenRecord { AccessToken = "at_only" };

		_store.Save("server-2", record);
		var loaded = _store.Load("server-2");

		Assert.NotNull(loaded);
		Assert.Equal("at_only", loaded!.AccessToken);
		Assert.Null(loaded.RefreshToken);
		Assert.Null(loaded.ExpiresAtUtc);
	}

	[Fact]
	public void Save_Overwrites_ExistingRecord()
	{
		_store.Save("server-3", new McpTokenRecord { AccessToken = "old" });
		_store.Save("server-3", new McpTokenRecord { AccessToken = "new" });

		var loaded = _store.Load("server-3");

		Assert.Equal("new", loaded!.AccessToken);
	}

	[Fact]
	public void Delete_RemovesStoredRecord_SubsequentLoadReturnsNull()
	{
		_store.Save("server-4", new McpTokenRecord { AccessToken = "at" });
		_store.Delete("server-4");

		Assert.Null(_store.Load("server-4"));
	}

	[Fact]
	public void Delete_NoStoredRecord_DoesNotThrow()
	{
		_store.Delete("never-saved");
	}

	[Fact]
	public void DifferentServerIds_AreStoredIndependently()
	{
		_store.Save("a", new McpTokenRecord { AccessToken = "token-a" });
		_store.Save("b", new McpTokenRecord { AccessToken = "token-b" });

		Assert.Equal("token-a", _store.Load("a")!.AccessToken);
		Assert.Equal("token-b", _store.Load("b")!.AccessToken);
	}

	[Theory]
	[InlineData("x\n")]
	[InlineData("x\r\n")]
	[InlineData(@"..\evil")]
	[InlineData("../evil")]
	[InlineData("a b")]
	[InlineData("")]
	public void AnIdOutsideTheRegistryCharset_IsRefusedBySave_AndIgnoredByLoadAndDelete(string id)
	{
		var outside = Path.Combine(_tempDir, "..", "evil.token");
		File.Delete(outside);                                                       // a leftover of an earlier run must not decide this one
		Assert.Throws<ArgumentException>(() => _store.Save(id, new McpTokenRecord { AccessToken = "at" }));

		Assert.Null(_store.Load(id));
		_store.Delete(id);                                                          // nothing to delete, and no exception
		Assert.False(File.Exists(outside));       // nothing was written outside the token directory
		Assert.Empty(Directory.GetFiles(_tempDir));
	}
}
