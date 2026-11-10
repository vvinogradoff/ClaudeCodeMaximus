using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TessynDesktop;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// <see cref="ClaudeSessionStatusService.AreSessionsResumable"/> must give exactly the per-session
/// <see cref="ClaudeSessionStatusService.IsSessionResumable"/> answers, across the default store, several
/// profile stores (one removed) and missing files (D2.1, perf-diag F3).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ClaudeSessionStatusServiceBatchTests : IDisposable
{
	private const string WorkDirA = @"C:\Projects\Alpha";
	private const string WorkDirB = @"C:\Projects\Beta Two\";
	private const string WorkDirC = @"D:\work\gamma.v2";

	private readonly string _tempRoot;
	private readonly List<string> _projectsDirs;
	private readonly ClaudeSessionStatusService _sut = new(null!);

	public ClaudeSessionStatusServiceBatchTests()
	{
		_tempRoot = Path.Combine(Path.GetTempPath(), "cmx-status-batch-" + Guid.NewGuid().ToString("N"));
		_projectsDirs =
		[
			Path.Combine(_tempRoot, "default", "projects"),
			Path.Combine(_tempRoot, "profile_1", "projects"),
			Path.Combine(_tempRoot, "profile_2_rem", "projects"),
			Path.Combine(_tempRoot, "profile_3_never_created", "projects"),
		];

		foreach (var dir in _projectsDirs.Take(3))
			Directory.CreateDirectory(dir);
	}

	public void Dispose() => Directory.Delete(_tempRoot, recursive: true);

	private void WriteJsonl(int dirIndex, string workingDirectory, string sessionId)
	{
		var slugDir = Path.Combine(_projectsDirs[dirIndex], Constants.ClaudeSessions.BuildProjectSlug(workingDirectory));
		Directory.CreateDirectory(slugDir);
		File.WriteAllText(Path.Combine(slugDir, sessionId + Constants.ClaudeSessions.SessionFileExtension), "{}");
	}

	[Fact]
	public void Batch_EqualsPerSessionResults_AcrossStoresAndMissingFiles()
	{
		WriteJsonl(0, WorkDirA, "in-default");
		WriteJsonl(1, WorkDirA, "in-profile-1");
		WriteJsonl(2, WorkDirB, "in-removed-profile");
		WriteJsonl(1, WorkDirC, "dotted-dir");
		WriteJsonl(2, WorkDirC, "dotted-dir-twice");
		WriteJsonl(0, WorkDirB, "wrong-dir-for-other");

		// A directory named like a session file must not count as a session.
		Directory.CreateDirectory(Path.Combine(_projectsDirs[0], Constants.ClaudeSessions.BuildProjectSlug(WorkDirA), "dir-not-file.jsonl"));

		var requests = new List<SessionResumabilityRequest>
		{
			new(WorkDirA, "in-default"),
			new(WorkDirA, "in-profile-1"),
			new(WorkDirB, "in-removed-profile"),
			new(WorkDirB, "wrong-dir-for-other"),
			new(WorkDirA, "wrong-dir-for-other"),
			new(WorkDirC, "dotted-dir"),
			new(WorkDirC, "dotted-dir-twice"),
			new(WorkDirA, "missing"),
			new(WorkDirA, "dir-not-file"),
			new(@"C:\Projects\Unknown", "in-default"),
			new(WorkDirA, null),
			new(WorkDirA, ""),
			new("", "in-default"),
			new(WorkDirA, "in-default"),
		};

		var batch = _sut.AreSessionsResumable(requests, _projectsDirs);

		var expected = requests
			.Select(r => r.ClaudeSessionId is not null && _sut.IsSessionResumable(r.WorkingDirectory, r.ClaudeSessionId, _projectsDirs))
			.ToList();

		Assert.Equal(expected, batch);
		Assert.Equal(7, batch.Count(b => b));
	}

	[Fact]
	public void Batch_SameSlugFromDifferentWorkingDirectories_MatchesPerSession()
	{
		// "a b" and "a/b" both slug to "a-b": a file under that slug counts for both, as it does per session.
		WriteJsonl(1, "a b", "shared");

		var batch = _sut.AreSessionsResumable([new("a b", "shared"), new("a/b", "shared")], _projectsDirs);

		Assert.Equal([true, true], batch);
		Assert.True(_sut.IsSessionResumable("a/b", "shared", _projectsDirs));
	}

	[Fact]
	public void Batch_EmptyRequests_ReturnsEmpty() =>
		Assert.Empty(_sut.AreSessionsResumable([], _projectsDirs));
}
