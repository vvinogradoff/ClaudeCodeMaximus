using System;
using System.IO;
using System.Text.Json.Nodes;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>The DPAPI token file, its one-time migration out of appsettings.json, and daemon.json (P1.C).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TdcpTokenAndDiscoveryTests : IDisposable
{
	private readonly string _dir = Path.Combine(Path.GetTempPath(), "TessynTdcpToken_" + Guid.NewGuid().ToString("N"));

	public TdcpTokenAndDiscoveryTests() => Directory.CreateDirectory(_dir);

	public void Dispose()
	{
		try { Directory.Delete(_dir, recursive: true); } catch { /* temp */ }
	}

	private string SettingsText
	{
		get
		{
			var path = Path.Combine(_dir, Constants.SettingsFileName);
			return File.Exists(path) ? File.ReadAllText(path) : "";
		}
	}

	[Fact]
	public void The_token_file_round_trips_and_is_not_plain_text()
	{
		var store = new TdcpTokenStore(Path.Combine(_dir, "token.dat"));

		store.Save("secret-token-value");

		Assert.Equal("secret-token-value", store.Load());
		Assert.DoesNotContain("secret-token-value", File.ReadAllText(store.FilePath, System.Text.Encoding.Latin1));
	}

	[Fact]
	public void A_missing_or_corrupt_token_file_loads_as_null()
	{
		var store = new TdcpTokenStore(Path.Combine(_dir, "token.dat"));
		Assert.Null(store.Load());

		File.WriteAllText(store.FilePath, "not a dpapi blob");
		Assert.Null(store.Load());
	}

	[Fact]
	public void A_token_file_that_stays_locked_fails_the_load_and_is_never_overwritten()
	{
		var store = new TdcpTokenStore(Path.Combine(_dir, "token.dat"));
		store.Save("kept-token");
		var before = File.ReadAllBytes(store.FilePath);

		using (new FileStream(store.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
		{
			Assert.Throws<IOException>(() => store.Load());
			Assert.Throws<IOException>(() => TdcpTokenProvider.GetOrCreate(store, new AppSettingsService(_dir)));
		}

		Assert.Equal(before, File.ReadAllBytes(store.FilePath));
		Assert.Equal("kept-token", store.Load());
	}

	[Fact]
	public void A_briefly_locked_token_file_is_read_after_a_retry()
	{
		var store = new TdcpTokenStore(Path.Combine(_dir, "token.dat"));
		store.Save("kept-token");

		var locked = new FileStream(store.FilePath, FileMode.Open, FileAccess.Read, FileShare.None);
		var release = new System.Threading.Thread(() =>
		{
			System.Threading.Thread.Sleep(120);
			locked.Dispose();
		});
		release.Start();

		Assert.Equal("kept-token", store.Load());
		release.Join();
	}

	[Fact]
	public void A_failing_token_save_degrades_to_an_in_memory_token_instead_of_throwing()
	{
		var settings = new AppSettingsService(_dir);
		settings.Settings.TdcpToken = "legacy-token-1234";
		settings.Save();

		var token = TdcpTokenProvider.GetOrCreate(new ThrowingSaveStore(), settings);

		Assert.Equal("legacy-token-1234", token);
		// The file does not hold the token, so the plain-text copy must stay.
		Assert.Equal("legacy-token-1234", settings.Settings.TdcpToken);

		var fresh = TdcpTokenProvider.GetOrCreate(new ThrowingSaveStore(), new AppSettingsService(Path.Combine(_dir, "other")));
		Assert.Matches("^[0-9a-f]{32}$", fresh);
	}

	private sealed class ThrowingSaveStore : ITdcpTokenStore
	{
		public string FilePath => "unwritable";
		public string? Load() => null;
		public void Save(string token) => throw new UnauthorizedAccessException("denied");
	}

	[Fact]
	public void An_existing_appsettings_token_is_migrated_unchanged_and_removed_from_appsettings()
	{
		var settings = new AppSettingsService(_dir);
		settings.Settings.TdcpToken = "legacy-token-1234";
		settings.Save();
		Assert.Contains("legacy-token-1234", SettingsText);
		var store = new TdcpTokenStore(Path.Combine(_dir, "token.dat"));

		var token = TdcpTokenProvider.GetOrCreate(store, settings);

		Assert.Equal("legacy-token-1234", token);
		Assert.Equal("legacy-token-1234", store.Load());
		Assert.Null(settings.Settings.TdcpToken);
		Assert.DoesNotContain("legacy-token-1234", SettingsText);
		Assert.DoesNotContain("\"tdcpToken\"", SettingsText, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void Without_any_token_a_new_one_is_issued_stored_and_stable_across_restarts()
	{
		var settings = new AppSettingsService(_dir);
		var store = new TdcpTokenStore(Path.Combine(_dir, "token.dat"));

		var first = TdcpTokenProvider.GetOrCreate(store, settings);
		var second = TdcpTokenProvider.GetOrCreate(store, new AppSettingsService(_dir));

		Assert.Matches("^[0-9a-f]{32}$", first);
		Assert.Equal(first, second);
		Assert.DoesNotContain(first, SettingsText);
	}

	[Fact]
	public void The_token_file_wins_over_a_stale_appsettings_value_which_is_still_cleared()
	{
		var settings = new AppSettingsService(_dir);
		var store = new TdcpTokenStore(Path.Combine(_dir, "token.dat"));
		store.Save("file-token");
		settings.Settings.TdcpToken = "old-plain-token";
		settings.Save();

		var token = TdcpTokenProvider.GetOrCreate(store, settings);

		Assert.Equal("file-token", token);
		Assert.DoesNotContain("old-plain-token", SettingsText);
	}

	[Fact]
	public void Daemon_json_describes_the_running_daemon_and_never_holds_the_token()
	{
		var store = new TdcpTokenStore(Path.Combine(_dir, "token.dat"));
		store.Save("never-in-daemon-json");
		var run = new DaemonRunInfo();
		var file = new DaemonDiscoveryFile(run, store, Path.Combine(_dir, "daemon.json"));

		file.Write(51234);

		var text = File.ReadAllText(file.FilePath);
		Assert.DoesNotContain("never-in-daemon-json", text);
		var json = JsonNode.Parse(text)!;
		Assert.Equal("http://127.0.0.1:51234/tdcp", json["endpoint"]!.GetValue<string>());
		Assert.Equal(Environment.ProcessId, json["pid"]!.GetValue<int>());
		Assert.Equal(Constants.Remote.ProtocolVersion, json["protocolVersion"]!.GetValue<int>());
		Assert.Equal(run.RunId, json["runId"]!.GetValue<string>());
		Assert.Equal(store.FilePath, json["tokenFile"]!.GetValue<string>());
		Assert.Equal(5, json.AsObject().Count);
	}

	[Fact]
	public void Clear_deletes_the_file_of_this_run_only()
	{
		var store = new TdcpTokenStore(Path.Combine(_dir, "token.dat"));
		var path = Path.Combine(_dir, "daemon.json");
		var older = new DaemonDiscoveryFile(new DaemonRunInfo(), store, path);
		var newer = new DaemonDiscoveryFile(new DaemonRunInfo(), store, path);

		older.Write(1111);
		newer.Write(2222);
		older.Clear();
		Assert.True(File.Exists(path), "a newer instance's file must survive the older one's exit");

		newer.Clear();
		Assert.False(File.Exists(path));
	}

	[Fact]
	public void Write_never_throws_for_an_unwritable_location()
	{
		var store = new TdcpTokenStore(Path.Combine(_dir, "token.dat"));
		var blocker = Path.Combine(_dir, "blocker");
		File.WriteAllText(blocker, "a file where a directory is needed");
		var file = new DaemonDiscoveryFile(new DaemonRunInfo(), store, Path.Combine(blocker, "daemon.json"));

		file.Write(1);
		file.Clear();
	}
}
