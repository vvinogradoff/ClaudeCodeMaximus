using System;
using System.IO;
using System.Text;
using System.Text.Json;
using TessynDesktop;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <remarks>Created by Claude</remarks>
public sealed class ClaudeSessionImportServiceTests : IDisposable
{
	private readonly string _tempDir;
	private readonly ClaudeSessionImportService _sut;

	public ClaudeSessionImportServiceTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), "cm_test_import_" + Path.GetRandomFileName());
		Directory.CreateDirectory(_tempDir);
		_sut = new ClaudeSessionImportService();
	}

	public void Dispose() => Directory.Delete(_tempDir, recursive: true);

	private string WriteJsonlFile(string sessionId, params string[] lines)
	{
		var path = Path.Combine(_tempDir, sessionId + Constants.ClaudeSessions.SessionFileExtension);
		File.WriteAllLines(path, lines, Encoding.UTF8);
		return path;
	}

	// --- ParseJsonlSession tests ---

	[Fact]
	public void ParseJsonlSession_ExtractsUserAndAssistantEntries()
	{
		var path = WriteJsonlFile("test-session",
			JsonLine("user", """{"role":"user","content":"Hello Claude"}""", "2026-03-10T10:00:00Z"),
			JsonLine("assistant", """{"content":[{"type":"text","text":"Hello! How can I help?"}]}""", "2026-03-10T10:00:05Z"));

		var entries = _sut.ParseJsonlSession(path);

		Assert.Equal(2, entries.Count);
		Assert.Equal(Constants.SessionFile.RoleUser, entries[0].Role);
		Assert.Equal("Hello Claude", entries[0].Content);
		Assert.Equal(Constants.SessionFile.RoleAssistant, entries[1].Role);
		Assert.Equal("Hello! How can I help?", entries[1].Content);
	}

	[Fact]
	public void ParseJsonlSession_ExtractsToolUseSummariesAsSystemEntries()
	{
		var path = WriteJsonlFile("test-tools",
			JsonLine("assistant", """{"content":[{"type":"text","text":"Let me check."},{"type":"tool_use","name":"Bash","input":{"command":"git status","description":"Check git status"}}]}""", "2026-03-10T10:00:00Z"));

		var entries = _sut.ParseJsonlSession(path);

		Assert.Equal(2, entries.Count);
		Assert.Equal(Constants.SessionFile.RoleAssistant, entries[0].Role);
		Assert.Equal("Let me check.", entries[0].Content);
		Assert.Equal(Constants.SessionFile.RoleSystem, entries[1].Role);
		Assert.Equal("[Tool: Bash] Check git status", entries[1].Content);
	}

	[Fact]
	public void ParseJsonlSession_SkipsThinkingBlocks()
	{
		var path = WriteJsonlFile("test-thinking",
			JsonLine("assistant", """{"content":[{"type":"thinking","thinking":"Let me think..."},{"type":"text","text":"Here is the answer."}]}""", "2026-03-10T10:00:00Z"));

		var entries = _sut.ParseJsonlSession(path);

		Assert.Single(entries);
		Assert.Equal("Here is the answer.", entries[0].Content);
	}

	[Fact]
	public void ParseJsonlSession_SkipsNonConversationEventTypes()
	{
		var path = WriteJsonlFile("test-skip",
			"""{"type":"file-history-snapshot","messageId":"abc","snapshot":{},"timestamp":"2026-03-10T10:00:00Z"}""",
			"""{"type":"progress","description":"Reading file","timestamp":"2026-03-10T10:00:01Z"}""",
			"""{"type":"queue-operation","timestamp":"2026-03-10T10:00:02Z"}""",
			JsonLine("user", """{"role":"user","content":"Real message"}""", "2026-03-10T10:00:03Z"));

		var entries = _sut.ParseJsonlSession(path);

		Assert.Single(entries);
		Assert.Equal("Real message", entries[0].Content);
	}

	[Fact]
	public void ParseJsonlSession_SkipsMalformedLines()
	{
		var path = WriteJsonlFile("test-corrupt",
			"this is not json",
			"{malformed json{{{",
			JsonLine("user", """{"role":"user","content":"Valid message"}""", "2026-03-10T10:00:00Z"));

		var entries = _sut.ParseJsonlSession(path);

		Assert.Single(entries);
		Assert.Equal("Valid message", entries[0].Content);
	}

	[Fact]
	public void ParseJsonlSession_EmptyFile_ReturnsEmptyList()
	{
		var path = WriteJsonlFile("test-empty");

		var entries = _sut.ParseJsonlSession(path);

		Assert.Empty(entries);
	}

	[Fact]
	public void ParseJsonlSession_PreservesTimestamps()
	{
		var path = WriteJsonlFile("test-timestamps",
			JsonLine("user", """{"role":"user","content":"Hello"}""", "2026-03-10T14:30:00Z"));

		var entries = _sut.ParseJsonlSession(path);

		Assert.Single(entries);
		Assert.Equal(new DateTimeOffset(2026, 3, 10, 14, 30, 0, TimeSpan.Zero), entries[0].Timestamp);
	}

	[Fact]
	public void ParseJsonlSession_ToolUseWithoutDescription_ShowsToolNameOnly()
	{
		var path = WriteJsonlFile("test-tool-nodesc",
			JsonLine("assistant", """{"content":[{"type":"tool_use","name":"Read","input":{"file_path":"/src/main.cs"}}]}""", "2026-03-10T10:00:00Z"));

		var entries = _sut.ParseJsonlSession(path);

		Assert.Single(entries);
		Assert.Equal(Constants.SessionFile.RoleSystem, entries[0].Role);
		Assert.Equal("[Tool: Read] /src/main.cs", entries[0].Content);
	}

	[Fact]
	public void ParseJsonlSession_UserContentAsArray_ExtractsText()
	{
		// Some Claude versions may use array format for user content
		var path = WriteJsonlFile("test-user-array",
			JsonLine("user", """{"role":"user","content":[{"type":"text","text":"Array format message"}]}""", "2026-03-10T10:00:00Z"));

		var entries = _sut.ParseJsonlSession(path);

		Assert.Single(entries);
		Assert.Equal("Array format message", entries[0].Content);
	}

	[Fact]
	public void ParseJsonlSession_StripsInjectedInstructions()
	{
		// Simulate a JSONL user message that contains Maximus-injected instruction block
		var augmented = "Hello Claude" + Constants.Instructions.Delimiter
		                + "\n- Once you have completed the request, commit all your changes to git.";
		var escaped = JsonEncodedText.Encode(augmented).ToString();
		var path = WriteJsonlFile("test-strip-instructions",
			JsonLine("user", $$"""{"role":"user","content":"{{escaped}}"}""", "2026-03-10T10:00:00Z"));

		var entries = _sut.ParseJsonlSession(path);

		Assert.Single(entries);
		Assert.Equal("Hello Claude", entries[0].Content);
	}

	// --- DiscoverSessions tests (uses the projectsDirsOverride constructor param to fake the
	// OS-default + per-profile "projects" roots without touching the real ~/.claude / %AppData%) ---

	[Fact]
	public void DiscoverSessions_FindsSessions_AcrossMultipleProjectsDirs()
	{
		var defaultDir = Path.Combine(_tempDir, "default", "projects");
		var removedProfileDir = Path.Combine(_tempDir, "profile_2_rem", "projects");
		Directory.CreateDirectory(defaultDir);
		Directory.CreateDirectory(removedProfileDir);

		var workingDirectory = @"C:\proj";
		var slug = Constants.ClaudeSessions.BuildProjectSlug(workingDirectory);

		WriteSlugJsonlFile(defaultDir, slug, "session-default",
			JsonLine("user", """{"role":"user","content":"Hi from default"}""", "2026-03-10T10:00:00Z"));
		// Simulates a session that only ever ran under a profile later removed (renamed "_rem").
		WriteSlugJsonlFile(removedProfileDir, slug, "session-removed-profile",
			JsonLine("user", """{"role":"user","content":"Hi from removed profile"}""", "2026-03-11T10:00:00Z"));

		var sut = new ClaudeSessionImportService([defaultDir, removedProfileDir]);
		var results = sut.DiscoverSessions(workingDirectory);

		Assert.Equal(2, results.Count);
		Assert.Contains(results, r => r.SessionId == "session-default");
		Assert.Contains(results, r => r.SessionId == "session-removed-profile");
	}

	[Fact]
	public void DiscoverSessions_ReturnsEmpty_WhenSlugMissingFromAllDirs()
	{
		var defaultDir = Path.Combine(_tempDir, "default", "projects");
		Directory.CreateDirectory(defaultDir);

		var sut = new ClaudeSessionImportService([defaultDir]);
		var results = sut.DiscoverSessions(@"C:\nonexistent-project");

		Assert.Empty(results);
	}

	private static void WriteSlugJsonlFile(string projectsDir, string slug, string sessionId, params string[] lines)
	{
		var slugDir = Path.Combine(projectsDir, slug);
		Directory.CreateDirectory(slugDir);
		File.WriteAllLines(Path.Combine(slugDir, sessionId + Constants.ClaudeSessions.SessionFileExtension), lines, Encoding.UTF8);
	}

	// --- Slug building tests (shared via Constants) ---

	[Fact]
	public void BuildProjectSlug_ReplacesNonAlphanumericWithDash()
	{
		var slug = Constants.ClaudeSessions.BuildProjectSlug("/Users/alice/Projects/my_app");
		Assert.Equal("-Users-alice-Projects-my-app", slug);
	}

	[Fact]
	public void BuildProjectSlug_TrimsTrailingSeparators()
	{
		var slug = Constants.ClaudeSessions.BuildProjectSlug("/Users/alice/Projects/my_app/");
		Assert.Equal("-Users-alice-Projects-my-app", slug);
	}

	[Fact]
	public void BuildProjectSlug_PreservesDashes()
	{
		var slug = Constants.ClaudeSessions.BuildProjectSlug("/my-project");
		Assert.Equal("-my-project", slug);
	}

	// --- Helper ---

	private static string JsonLine(string type, string messageJson, string timestamp)
	{
		return $$"""{"type":"{{type}}","message":{{messageJson}},"timestamp":"{{timestamp}}","uuid":"test","sessionId":"test-session"}""";
	}
}
