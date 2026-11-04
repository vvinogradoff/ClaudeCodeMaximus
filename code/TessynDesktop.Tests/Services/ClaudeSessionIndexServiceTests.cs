using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

// ── Fake ──────────────────────────────────────────────────────────────────────

internal sealed class FakeClaudeAssistService : IClaudeAssistService
{
    public Dictionary<string, string> TitlesToReturn { get; set; } = new();
    public int GenerateTitlesCallCount { get; private set; }
    public List<ClaudeSessionSummaryModel>? LastSummaries { get; private set; }

    public Task<Dictionary<string, string>> GenerateTitlesAsync(
        List<ClaudeSessionSummaryModel> summaries,
        Action<Dictionary<string, string>>? onBatchComplete = null,
        CancellationToken cancellationToken = default)
    {
        GenerateTitlesCallCount++;
        LastSummaries = summaries;
        var result = summaries
            .Where(s => TitlesToReturn.ContainsKey(s.SessionId))
            .ToDictionary(s => s.SessionId, s => TitlesToReturn[s.SessionId]);
        onBatchComplete?.Invoke(result);
        return Task.FromResult(result);
    }

    public Task<List<string>> SearchSessionsAsync(
        List<ClaudeSessionSummaryModel> summaries, string query, CancellationToken cancellationToken = default)
        => Task.FromResult(new List<string>());
}

// ── Tests ───────────────────────────────────────────────────────────────────────

/// <remarks>Created by Claude</remarks>
public sealed class ClaudeSessionIndexServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _projectsDir;
    private readonly FakeClaudeAssistService _assistService = new();
    private readonly ClaudeSessionIndexService _service;

    public ClaudeSessionIndexServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "cmx-index-tests-" + Guid.NewGuid().ToString("N"));
        _projectsDir = Path.Combine(_tempRoot, "projects");
        Directory.CreateDirectory(_projectsDir);
        var dbPath = Path.Combine(_tempRoot, "index.db");

        _service = new ClaudeSessionIndexService(new ClaudeSessionImportService(), _assistService, dbPath, [_projectsDir]);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { /* best effort cleanup */ }
    }

    /// <summary>Writes a minimal but realistic Claude Code JSONL session file.</summary>
    private string WriteSession(string slug, string externalId, params (string Role, string Content)[] messages)
    {
        var slugDir = Path.Combine(_projectsDir, slug);
        Directory.CreateDirectory(slugDir);
        var path = Path.Combine(slugDir, externalId + ".jsonl");

        var lines = messages.Select((m, i) =>
        {
            var timestamp = $"2026-01-01T00:{i:D2}:00Z";
            return m.Role == "user"
                ? "{\"type\":\"user\",\"timestamp\":\"" + timestamp + "\",\"message\":{\"role\":\"user\",\"content\":\"" + m.Content + "\"}}"
                : "{\"type\":\"assistant\",\"timestamp\":\"" + timestamp + "\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"" + m.Content + "\"}]}}";
        });

        File.WriteAllLines(path, lines);
        return path;
    }

    /// <summary>Writes a session file with a single user turn carrying one tool_result block.</summary>
    private string WriteToolResultSession(string slug, string externalId, string toolResultContent)
    {
        var slugDir = Path.Combine(_projectsDir, slug);
        Directory.CreateDirectory(slugDir);
        var path = Path.Combine(slugDir, externalId + ".jsonl");
        var line = """{"type":"user","timestamp":"2026-01-01T00:00:00Z","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"toolu_1","content":"""
            + JsonSerializer.Serialize(toolResultContent) + "}]}}";
        File.WriteAllLines(path, [line]);
        return path;
    }

    // ── Reindex ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReindexAsync_IndexesSessionsAcrossMultipleProjects()
    {
        WriteSession("-Users-x-project-a", "session-1", ("user", "Deploy the config to production"));
        WriteSession("-Users-x-project-b", "session-2", ("user", "Fix the login bug"));

        var count = await _service.ReindexAsync();

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task ReindexAsync_SkipsUnchangedFiles_OnSecondRun()
    {
        WriteSession("-Users-x-project-a", "session-1", ("user", "Hello world"));

        var first = await _service.ReindexAsync();
        var second = await _service.ReindexAsync();

        Assert.Equal(1, first);
        Assert.Equal(0, second); // mtime unchanged, nothing to reindex
    }

    [Fact]
    public async Task ReindexAsync_ReindexesFile_WhenModifiedAfterFirstIndex()
    {
        var path = WriteSession("-Users-x-project-a", "session-1", ("user", "Original content"));
        await _service.ReindexAsync();

        // Modify content and bump mtime so the incremental check sees it as changed
        await Task.Delay(10);
        File.WriteAllLines(path, new[]
        {
            """{"type":"user","timestamp":"2026-01-01T00:00:00Z","message":{"role":"user","content":"Updated content"}}""",
        });
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow);

        var second = await _service.ReindexAsync();
        Assert.Equal(1, second);

        var results = await _service.SearchAsync("Updated");
        Assert.Single(results);
        var stale = await _service.SearchAsync("Original");
        Assert.Empty(stale);
    }

    [Fact]
    public async Task ReindexAsync_ReturnsZero_WhenNoProjectsDirectory()
    {
        var emptyDir = Path.Combine(_tempRoot, "does-not-exist");
        var service = new ClaudeSessionIndexService(
            new ClaudeSessionImportService(), _assistService,
            Path.Combine(_tempRoot, "other.db"), [emptyDir]);

        var count = await service.ReindexAsync();

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task ReindexAsync_IndexesToolResultContent()
    {
        WriteToolResultSession("-Users-x-project-a", "session-1", "the research findings mention Visual Studio integration");

        await _service.ReindexAsync();

        var results = await _service.SearchAsync("Visual Studio");
        var result = Assert.Single(results);
        Assert.Equal("TOOL_RESULT", result.Role);
    }

    [Fact]
    public async Task ReindexAsync_RebuildsAlreadyIndexedSessions_WhenIndexContentVersionIsBehind()
    {
        // Simulate a database left over from before tool_result indexing existed: the session row
        // is already present with a matching mtime (so the plain incremental check would skip it)
        // but the schema's user_version predates the current content version.
        var path = WriteToolResultSession("-Users-x-project-a", "session-1", "needle content");
        var mtimeTicks = File.GetLastWriteTimeUtc(path).Ticks;
        var dbPath = Path.Combine(_tempRoot, "stale-version.db");

        using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            conn.Open();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    CREATE TABLE sessions (
                        external_id TEXT PRIMARY KEY, project_slug TEXT NOT NULL, jsonl_path TEXT NOT NULL,
                        mtime_ticks INTEGER NOT NULL, title TEXT, title_generated_at_ticks INTEGER,
                        first_user_prompt TEXT, message_count INTEGER NOT NULL,
                        created_ticks INTEGER NOT NULL, last_used_ticks INTEGER NOT NULL);
                    CREATE VIRTUAL TABLE messages USING fts5(content, role UNINDEXED, external_id UNINDEXED,
                        project_slug UNINDEXED, message_index UNINDEXED, timestamp_ticks UNINDEXED);
                    INSERT INTO sessions (external_id, project_slug, jsonl_path, mtime_ticks, message_count, created_ticks, last_used_ticks)
                    VALUES ('session-1', '-Users-x-project-a', @path, @mtime, 1, 0, 0);
                    """;
                cmd.Parameters.AddWithValue("@path", path);
                cmd.Parameters.AddWithValue("@mtime", mtimeTicks);
                cmd.ExecuteNonQuery();
            }
            using var versionCmd = conn.CreateCommand();
            versionCmd.CommandText = "PRAGMA user_version = 1;";
            versionCmd.ExecuteNonQuery();
        }

        var service = new ClaudeSessionIndexService(new ClaudeSessionImportService(), _assistService, dbPath, [_projectsDir]);
        var count = await service.ReindexAsync();

        Assert.Equal(1, count); // rebuilt despite matching mtime, because the stored version was behind
        var results = await service.SearchAsync("needle");
        Assert.Single(results);
    }

    // ── Search ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_FindsMatchingContent_AcrossProjects()
    {
        WriteSession("-Users-x-project-a", "session-1", ("user", "Deploy the config to production"));
        WriteSession("-Users-x-project-b", "session-2", ("user", "Fix the login bug"));
        await _service.ReindexAsync();

        var results = await _service.SearchAsync("deploy");

        Assert.Single(results);
        Assert.Equal("session-1", results[0].ExternalId);
        Assert.Equal("-Users-x-project-a", results[0].ProjectSlug);
    }

    [Fact]
    public async Task SearchAsync_FiltersByProjectSlug()
    {
        WriteSession("-Users-x-project-a", "session-1", ("user", "shared keyword here"));
        WriteSession("-Users-x-project-b", "session-2", ("user", "shared keyword here"));
        await _service.ReindexAsync();

        var results = await _service.SearchAsync("keyword", projectSlug: "-Users-x-project-b");

        Assert.Single(results);
        Assert.Equal("session-2", results[0].ExternalId);
    }

    [Fact]
    public async Task SearchAsync_FiltersByRole()
    {
        WriteSession("-Users-x-project-a", "session-1",
            ("user", "find this phrase"),
            ("assistant", "find this phrase too"));
        await _service.ReindexAsync();

        var userResults = await _service.SearchAsync("find this phrase", role: "USER");

        Assert.Single(userResults);
        Assert.Equal("USER", userResults[0].Role);
    }

    [Fact]
    public async Task SearchAsync_ReturnsEmpty_WhenNoMatch()
    {
        WriteSession("-Users-x-project-a", "session-1", ("user", "hello world"));
        await _service.ReindexAsync();

        var results = await _service.SearchAsync("nonexistent");

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_DoesNotThrow_OnUnbalancedQuoteInQuery()
    {
        WriteSession("-Users-x-project-a", "session-1", ("user", "hello world"));
        await _service.ReindexAsync();

        var results = await _service.SearchAsync("hello \" world");

        Assert.NotNull(results);
    }

    [Fact]
    public async Task SearchAsync_ReturnsEmpty_ForEmptyQuery()
    {
        var results = await _service.SearchAsync("   ");
        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_PrefersExactPhraseMatches_OverIncidentalCoOccurrence()
    {
        // "visual" and "studio" both present but not adjacent — an AND-of-terms match, not a phrase.
        WriteSession("-Users-x-project-a", "session-1", ("user", "our visual identity redesign uses a new color studio"));
        // Genuine adjacent phrase match.
        WriteSession("-Users-x-project-a", "session-2", ("user", "we should use visual studio for this"));
        await _service.ReindexAsync();

        var results = await _service.SearchAsync("visual studio");

        var result = Assert.Single(results);
        Assert.Equal("session-2", result.ExternalId);
    }

    [Fact]
    public async Task SearchAsync_FallsBackToTermCoOccurrence_WhenNoPhraseMatchExists()
    {
        WriteSession("-Users-x-project-a", "session-1", ("user", "our visual identity redesign uses a new color studio"));
        await _service.ReindexAsync();

        var results = await _service.SearchAsync("visual studio");

        Assert.Single(results);
    }

    // ── SearchMatchingSessionIdsAsync (unranked, uncapped tree-filter lookup) ─────────

    [Fact]
    public async Task SearchMatchingSessionIdsAsync_ReturnsEveryMatch_EvenBeyondSearchAsyncsDefaultCap()
    {
        for (var i = 0; i < 55; i++)
            WriteSession("-Users-x-project-a", $"session-{i}", ("user", $"keyword occurrence number {i}"));
        await _service.ReindexAsync();

        var ids = await _service.SearchMatchingSessionIdsAsync("keyword");

        Assert.Equal(55, ids.Count); // exceeds SearchAsync's default 50-row LIMIT
    }

    [Fact]
    public async Task SearchMatchingSessionIdsAsync_ReturnsEmpty_ForEmptyQuery()
    {
        var ids = await _service.SearchMatchingSessionIdsAsync("   ");
        Assert.Empty(ids);
    }

    [Fact]
    public async Task SearchMatchingSessionIdsAsync_ReturnsEmpty_WhenNoMatch()
    {
        WriteSession("-Users-x-project-a", "session-1", ("user", "hello world"));
        await _service.ReindexAsync();

        var ids = await _service.SearchMatchingSessionIdsAsync("nonexistent");

        Assert.Empty(ids);
    }

    [Fact]
    public async Task SearchMatchingSessionIdsAsync_ScopesToProjectSlug_WhenProvided()
    {
        WriteSession("-Users-x-project-a", "session-a", ("user", "keyword in project a"));
        WriteSession("-Users-x-project-b", "session-b", ("user", "keyword in project b"));
        await _service.ReindexAsync();

        var ids = await _service.SearchMatchingSessionIdsAsync("keyword", "-Users-x-project-a");

        Assert.Equal(["session-a"], ids);
    }

    // ── Titles ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EnsureTitlesAsync_GeneratesAndPersistsTitle_WhenNotCached()
    {
        WriteSession("-Users-x-project-a", "session-1", ("user", "Deploy the config"));
        await _service.ReindexAsync();

        _assistService.TitlesToReturn["session-1"] = "Deploying configuration";
        var summaries = await _service.GetSessionSummariesAsync(["session-1"]);

        var titles = await _service.EnsureTitlesAsync(summaries);

        Assert.Equal("Deploying configuration", titles["session-1"]);
        Assert.Equal(1, _assistService.GenerateTitlesCallCount);
    }

    [Fact]
    public async Task EnsureTitlesAsync_ReturnsCachedTitle_WithoutRegenerating()
    {
        WriteSession("-Users-x-project-a", "session-1", ("user", "Deploy the config"));
        await _service.ReindexAsync();
        var summaries = await _service.GetSessionSummariesAsync(["session-1"]);

        _assistService.TitlesToReturn["session-1"] = "First generation";
        var first = await _service.EnsureTitlesAsync(summaries);
        Assert.Equal("First generation", first["session-1"]);
        Assert.Equal(1, _assistService.GenerateTitlesCallCount);

        // Second call should hit the persisted cache and never call the assist service again,
        // even though TitlesToReturn now points at a different value.
        _assistService.TitlesToReturn["session-1"] = "Should not be used";
        var second = await _service.EnsureTitlesAsync(summaries);

        Assert.Equal("First generation", second["session-1"]);
        Assert.Equal(1, _assistService.GenerateTitlesCallCount);
    }

    [Fact]
    public async Task EnsureTitlesAsync_ReturnsEmpty_WhenAssistServiceHasNoTitle()
    {
        WriteSession("-Users-x-project-a", "session-1", ("user", "Deploy the config"));
        await _service.ReindexAsync();
        var summaries = await _service.GetSessionSummariesAsync(["session-1"]);

        // _assistService.TitlesToReturn is empty — simulates CLI unavailable/failed
        var titles = await _service.EnsureTitlesAsync(summaries);

        Assert.Empty(titles);
    }

    // ── Session summaries ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSessionSummariesAsync_ReturnsIndexedMetadata()
    {
        WriteSession("-Users-x-project-a", "session-1", ("user", "First message here"));
        await _service.ReindexAsync();

        var summaries = await _service.GetSessionSummariesAsync(["session-1"]);

        var summary = Assert.Single(summaries);
        Assert.Equal("session-1", summary.SessionId);
        Assert.Equal("-Users-x-project-a", summary.ProjectSlug);
        Assert.Equal("First message here", summary.FirstUserPrompt);
        Assert.Equal(1, summary.MessageCount);
    }

    [Fact]
    public async Task GetSessionSummariesAsync_ReturnsEmpty_ForUnknownIds()
    {
        var summaries = await _service.GetSessionSummariesAsync(["never-indexed"]);
        Assert.Empty(summaries);
    }
}
