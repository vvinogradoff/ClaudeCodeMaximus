using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Agent;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>TC.15.5 and the <c>ResolveNodeId_*</c> tests of TC.15.6 (FR.15.13, FR.21.8).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionRegistryListingTests : IDisposable
{
	private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(2);

	private readonly string _tempDir;
	private readonly AppSettingsService _settings;
	private readonly FakeSessionFileService _fileService = new();
	private readonly FakeClaudeSessionStatusService _sessionStatus = new();
	private readonly SessionRegistry _registry;
	private readonly DirectoryNodeModel _dir;

	public SessionRegistryListingTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), "cm_test_" + Path.GetRandomFileName());
		Directory.CreateDirectory(_tempDir);
		_settings = new AppSettingsService(_tempDir);
		_registry = new SessionRegistry(_settings, _fileService, new FakeDirectoryLabelService(), _sessionStatus);
		_dir = _registry.AddDirectory(Path.Combine(_tempDir, "dir"));
	}

	public void Dispose() => Directory.Delete(_tempDir, recursive: true);

	private SessionNodeModel AddSession(string name, string? claudeSessionId = null, DateTimeOffset? lastWrite = null)
	{
		var session = _registry.AddSession(_dir, name);
		session.ClaudeSessionId = claudeSessionId;
		if (lastWrite != null)
			_fileService.LastWriteTimes[session.FileName] = lastWrite.Value;
		return session;
	}

	/// <summary>Runs on another thread, so it only succeeds when the calling thread is not inside the settings lock.</summary>
	private bool CanAcquireSettingsLockFromAnotherThread() =>
		Task.Run(() =>
		{
			if (!Monitor.TryEnter(_settings.SyncRoot, LockTimeout))
				return false;

			Monitor.Exit(_settings.SyncRoot);
			return true;
		}).Result;

	// ── TC.15.5 ───────────────────────────────────────────────────────────────

	[Fact]
	public void QueryAgentSessions_NeverReadsSessionFileContents()
	{
		var caller = AddSession("Caller", "claude-1");
		AddSession("Other", "claude-2");
		_fileService.Entries[caller.FileName] = new[]
		{
			new SessionEntryModel { Timestamp = DateTimeOffset.UtcNow, Role = Constants.SessionFile.RoleUser, Content = "x" },
		};

		var page = _registry.QueryAgentSessions(new SessionListQueryModel(), caller.NodeId);

		Assert.Equal(2, page.Rows.Count);
		Assert.Equal(0, _fileService.ReadEntriesCalls);
	}

	[Fact]
	public void QueryAgentSessions_LastActivityIsLastWriteTime()
	{
		var written = new DateTimeOffset(2026, 10, 1, 8, 30, 0, TimeSpan.Zero);
		var caller  = AddSession("Caller", lastWrite: written);
		var missing = AddSession("NoFile");
		_fileService.Entries[caller.FileName] = new[]
		{
			new SessionEntryModel { Timestamp = written.AddDays(30), Role = Constants.SessionFile.RoleUser, Content = "x" },
		};

		var page = _registry.QueryAgentSessions(new SessionListQueryModel(), caller.NodeId);

		Assert.Equal(written, page.Rows.Single(r => r.NodeId == caller.NodeId).LastActivityUtc);
		Assert.Null(page.Rows.Single(r => r.NodeId == missing.NodeId).LastActivityUtc);
	}

	[Fact]
	public void QueryAgentSessions_ResumabilityCheckedOnlyForPageRows_OneProjectsDirListPerCall()
	{
		var start  = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
		var newest = AddSession("S1", "claude-1", start.AddHours(5));
		var second = AddSession("S2", "claude-2", start.AddHours(4));
		AddSession("S3", "claude-3", start.AddHours(3));
		AddSession("S4", "claude-4", start.AddHours(2));
		AddSession("S5", null, start.AddHours(1));
		_sessionStatus.ResumableSessionIds.Add("claude-1");

		var page = _registry.QueryAgentSessions(new SessionListQueryModel { Limit = 2 }, newest.NodeId);

		Assert.Equal(new[] { newest.NodeId, second.NodeId }, page.Rows.Select(r => r.NodeId));
		Assert.Equal(5, page.TotalRows);
		Assert.Equal(new[] { "claude-1", "claude-2" }, _sessionStatus.ResumabilityChecks);
		Assert.Equal(1, _sessionStatus.GetAllProjectsDirsCalls);
		Assert.All(_sessionStatus.ResumabilityProjectsDirs, dirs => Assert.NotNull(dirs));
		Assert.Single(_sessionStatus.ResumabilityProjectsDirs.Distinct());
		Assert.True(page.Rows[0].IsResumable);
		Assert.False(page.Rows[1].IsResumable);
	}

	[Fact]
	public void QueryAgentSessions_NoClaudeSessionId_NotResumableAndNotChecked()
	{
		var caller = AddSession("Caller", null);

		var page = _registry.QueryAgentSessions(new SessionListQueryModel(), caller.NodeId);

		Assert.False(Assert.Single(page.Rows).IsResumable);
		Assert.Empty(_sessionStatus.ResumabilityChecks);
	}

	[Fact]
	public void QueryAgentSessions_SettingsLockNotHeldDuringFileSystemWork()
	{
		var caller = AddSession("Caller", "claude-1", DateTimeOffset.UtcNow);
		var acquired          = false;
		var acquiredResumable = false;
		_fileService.OnGetLastWriteTime = _ => acquired = CanAcquireSettingsLockFromAnotherThread();
		_sessionStatus.OnIsSessionResumable = _ => acquiredResumable = CanAcquireSettingsLockFromAnotherThread();

		_registry.QueryAgentSessions(new SessionListQueryModel(), caller.NodeId);

		Assert.True(acquired);
		Assert.True(acquiredResumable);
	}

	[Fact]
	public void QueryAgentSessions_UnknownRootNodeId_EmptyPage()
	{
		var caller = AddSession("Caller");

		var page = _registry.QueryAgentSessions(
			new SessionListQueryModel { Scope = SessionListScope.Children, RootNodeId = "does-not-exist" }, caller.NodeId);

		Assert.Empty(page.Rows);
		Assert.Empty(page.Sections);
	}

	[Fact]
	public void QueryAgentSessions_SnapshotCarriesGroupPathAndSupervisor()
	{
		var group  = _registry.AddGroup(_dir, "Infra");
		var nested = _registry.AddGroupToGroup(group, "Prod");
		var caller = AddSession("Caller");
		var inGroup = _registry.AddSessionToGroup(nested, "Deep");

		var page = _registry.QueryAgentSessions(new SessionListQueryModel { Query = "prod" }, caller.NodeId);

		Assert.Equal(inGroup.NodeId, Assert.Single(page.Rows).NodeId);
	}

	[Fact]
	public void BuildAgentSessionSummaries_LastActivityIsLastWriteTime_NoContentReads()
	{
		var written = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
		var session = AddSession("FavSession", lastWrite: written);
		session.IsFavorite = true;
		_fileService.Entries[session.FileName] = new[]
		{
			new SessionEntryModel { Timestamp = written.AddDays(9), Role = Constants.SessionFile.RoleAssistant, Content = "last" },
		};

		var summary = Assert.Single(_registry.BuildAgentSessionSummaries());

		Assert.True(summary.IsFavorite);
		Assert.Equal(written, summary.LastActivityUtc);
		Assert.Equal(0, _fileService.ReadEntriesCalls);
	}

	[Fact]
	public void BuildAgentSessionSummaries_ReturnsEverySession_ResumabilityUsesOneProjectsDirList()
	{
		AddSession("S1", "claude-1");
		AddSession("S2", "claude-2");
		AddSession("S3", null);
		_sessionStatus.ResumableSessionIds.Add("claude-2");

		var summaries = _registry.BuildAgentSessionSummaries();

		Assert.Equal(3, summaries.Count);
		Assert.Equal(1, _sessionStatus.GetAllProjectsDirsCalls);
		Assert.Single(_sessionStatus.ResumabilityProjectsDirs.Distinct());
		Assert.True(summaries.Single(s => s.Name == "S2").IsResumable);
		Assert.False(summaries.Single(s => s.Name == "S1").IsResumable);
		Assert.False(summaries.Single(s => s.Name == "S3").IsResumable);
	}

	[Fact]
	public void BuildAgentSessionSummaries_SettingsLockNotHeldDuringFileSystemWork()
	{
		AddSession("S1", "claude-1", DateTimeOffset.UtcNow);
		var acquired          = false;
		var acquiredResumable = false;
		_fileService.OnGetLastWriteTime = _ => acquired = CanAcquireSettingsLockFromAnotherThread();
		_sessionStatus.OnIsSessionResumable = _ => acquiredResumable = CanAcquireSettingsLockFromAnotherThread();

		_registry.BuildAgentSessionSummaries();

		Assert.True(acquired);
		Assert.True(acquiredResumable);
	}

	// ── TC.15.6 ───────────────────────────────────────────────────────────────

	[Fact]
	public void ResolveNodeId_FullId_CaseInsensitive()
	{
		var session = AddSession("S");
		session.NodeId = "abcdef0123456789abcdef0123456789";

		var result = _registry.ResolveNodeId("ABCDEF0123456789ABCDEF0123456789");

		Assert.Equal(NodeIdResolutionStatus.Resolved, result.Status);
		Assert.Equal("abcdef0123456789abcdef0123456789", result.NodeId);
	}

	[Fact]
	public void ResolveNodeId_UniquePrefix_Resolved()
	{
		AddSession("S1").NodeId = "abcdef0123456789abcdef0123456789";
		AddSession("S2").NodeId = "fedcba9876543210fedcba9876543210";

		var result = _registry.ResolveNodeId("ABCDEF01");

		Assert.Equal(NodeIdResolutionStatus.Resolved, result.Status);
		Assert.Equal("abcdef0123456789abcdef0123456789", result.NodeId);
	}

	[Fact]
	public void ResolveNodeId_AmbiguousPrefix_ReturnsUpTo5Matches()
	{
		for (var i = 0; i < 7; i++)
			AddSession($"S{i}").NodeId = $"abcdef{i}123456789abcdef012345678";
		AddSession("Other").NodeId = "fedcba9876543210fedcba9876543210";

		var result = _registry.ResolveNodeId("abcdef");

		Assert.Equal(NodeIdResolutionStatus.Ambiguous, result.Status);
		Assert.Null(result.NodeId);
		Assert.Equal(7, result.MatchCount);
		Assert.Equal(Constants.Agent.AmbiguousNodeIdMaxMatches, result.Matches.Count);
		Assert.All(result.Matches, m => Assert.StartsWith("abcdef", m.NodeId));
		Assert.All(result.Matches, m => Assert.False(string.IsNullOrEmpty(m.Name)));
	}

	[Fact]
	public void ResolveNodeId_PrefixShorterThan6_TooShort()
	{
		AddSession("S").NodeId = "abcdef0123456789abcdef0123456789";

		var result = _registry.ResolveNodeId("abcde");

		Assert.Equal(NodeIdResolutionStatus.TooShort, result.Status);
	}

	[Fact]
	public void ResolveNodeId_Unknown_NotFound()
	{
		AddSession("S").NodeId = "abcdef0123456789abcdef0123456789";

		var result = _registry.ResolveNodeId("123456");

		Assert.Equal(NodeIdResolutionStatus.NotFound, result.Status);
		Assert.Null(result.NodeId);
		Assert.Equal(0, result.MatchCount);
	}
}
