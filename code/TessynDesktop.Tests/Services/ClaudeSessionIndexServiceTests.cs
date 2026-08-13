using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
