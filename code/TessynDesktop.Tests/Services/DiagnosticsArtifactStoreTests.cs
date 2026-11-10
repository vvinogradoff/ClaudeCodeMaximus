using System;
using System.IO;
using System.Linq;
using TessynDesktop.Models;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>Diag folder naming and retention (D1.4, D1.6).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class DiagnosticsArtifactStoreTests : IDisposable
{
	private readonly DiagnosticsTestEnv _env = new();

	public void Dispose() => _env.Dispose();

	private void Seed(string prefix, string extension, int count)
	{
		Directory.CreateDirectory(_env.Folder);
		for (var i = 0; i < count; i++)
		{
			var path = Path.Combine(_env.Folder, $"{prefix}{i:D3}{extension}");
			File.WriteAllText(path, "x");
			File.SetLastWriteTimeUtc(path, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i));
		}
	}

	[Fact]
	public void CreatePath_UsesKindPrefixExtensionAndTimestamp()
	{
		var hang = _env.Store.CreatePath(DiagnosticArtifactKind.Hang);
		var dump = _env.Store.CreatePath(DiagnosticArtifactKind.Dump);

		Assert.StartsWith("hang-", Path.GetFileName(hang));
		Assert.EndsWith(".txt", hang);
		Assert.StartsWith("dump-", Path.GetFileName(dump));
		Assert.EndsWith(".dmp", dump);
		Assert.True(Directory.Exists(_env.Folder));
	}

	[Fact]
	public void EnforceRetention_KeepsNewestTextArtifactsPerKind()
	{
		Seed("hang-", ".txt", 25);
		Seed("heap-", ".txt", 5);

		_env.Store.EnforceRetention(DiagnosticArtifactKind.Hang);

		var hangs = Directory.GetFiles(_env.Folder, "hang-*").Select(Path.GetFileName).OrderBy(n => n).ToList();
		Assert.Equal(_env.Settings.ArtifactRetentionCount, hangs.Count);
		Assert.Equal("hang-005.txt", hangs.First());
		Assert.Equal("hang-024.txt", hangs.Last());
		Assert.Equal(5, Directory.GetFiles(_env.Folder, "heap-*").Length);
	}

	[Fact]
	public void EnforceRetention_KeepsConfiguredNumberOfDumps()
	{
		Seed("dump-", ".dmp", 6);
		Seed("hang-", ".txt", 6);

		_env.Store.EnforceRetention(DiagnosticArtifactKind.Dump);

		var dumps = Directory.GetFiles(_env.Folder, "dump-*").Select(Path.GetFileName).OrderBy(n => n).ToList();
		Assert.Equal(_env.Settings.DumpRetentionCount, dumps.Count);
		Assert.Equal("dump-003.dmp", dumps.First());
		Assert.Equal(6, Directory.GetFiles(_env.Folder, "hang-*").Length);
	}

	[Fact]
	public void EnforceRetention_MissingFolder_DoesNothing()
	{
		_env.Store.EnforceRetention(DiagnosticArtifactKind.Hang);

		Assert.False(Directory.Exists(_env.Folder));
	}
}
