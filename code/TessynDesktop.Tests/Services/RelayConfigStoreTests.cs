using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <remarks>Created by Claude</remarks>
public sealed class RelayConfigStoreTests : IDisposable
{
	private readonly string _tempDir =
		Path.Combine(Path.GetTempPath(), "TessynRelayConfigStoreTests_" + Guid.NewGuid().ToString("N"));
	private readonly string _path;
	private readonly RelayConfigStore _store;

	public RelayConfigStoreTests()
	{
		_path = Path.Combine(_tempDir, "web-relay.dat");
		_store = new RelayConfigStore(_path);
	}

	public void Dispose()
	{
		try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort cleanup */ }
	}

	[Fact]
	public void Load_Missing_ReturnsNull() => Assert.Null(_store.Load());

	[Fact]
	public void SaveThenLoad_RoundTrips()
	{
		_store.Save(new RelayConfigModel { Enabled = true, Url = "https://example.test", Token = "tok-123" });

		var loaded = _store.Load();

		Assert.NotNull(loaded);
		Assert.True(loaded.Enabled);
		Assert.Equal("https://example.test", loaded.Url);
		Assert.Equal("tok-123", loaded.Token);
		Assert.False(File.Exists(_path + ".tmp"));
	}

	[Fact]
	public void Save_Overwrites()
	{
		_store.Save(new RelayConfigModel { Enabled = true, Url = "https://a.test", Token = "one" });
		_store.Save(new RelayConfigModel { Enabled = false, Url = "https://b.test", Token = "two" });

		var loaded = _store.Load()!;
		Assert.False(loaded.Enabled);
		Assert.Equal("two", loaded.Token);
	}

	[Fact]
	public void File_IsDpapiProtectedUtf8CamelCaseJson_AsProvisionedFromPowerShell()
	{
		_store.Save(new RelayConfigModel { Enabled = true, Url = "https://example.test", Token = "tok" });

		var plaintext = Encoding.UTF8.GetString(
			ProtectedData.Unprotect(File.ReadAllBytes(_path), optionalEntropy: null, DataProtectionScope.CurrentUser));

		Assert.Equal("""{"enabled":true,"url":"https://example.test","token":"tok"}""", plaintext);
	}

	[Fact]
	public void Load_ReadsAFileWrittenWithTheSpecifiedFormat()
	{
		var json = """{ "enabled": true, "url": "https://x.test", "token": "abc" }""";
		var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(json), optionalEntropy: null, DataProtectionScope.CurrentUser);
		File.WriteAllBytes(_path, encrypted);

		var loaded = _store.Load()!;

		Assert.True(loaded.Enabled);
		Assert.Equal("abc", loaded.Token);
	}

	[Fact]
	public void Load_CorruptFile_ReturnsNull()
	{
		File.WriteAllBytes(_path, [1, 2, 3, 4, 5]);

		Assert.Null(_store.Load());
	}

	[Fact]
	public void Load_ValidDpapiButNotJson_ReturnsNull()
	{
		File.WriteAllBytes(_path, ProtectedData.Protect(Encoding.UTF8.GetBytes("not json"), null, DataProtectionScope.CurrentUser));

		Assert.Null(_store.Load());
	}

	[Fact]
	public void Delete_RemovesFile_AndIsIdempotent()
	{
		_store.Save(new RelayConfigModel { Enabled = true, Url = "https://x.test", Token = "t" });

		_store.Delete();
		_store.Delete();

		Assert.Null(_store.Load());
		Assert.False(File.Exists(_path));
	}
}
