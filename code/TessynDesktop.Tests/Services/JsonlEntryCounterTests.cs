using System;
using System.IO;
using System.Linq;
using System.Text;
using TessynDesktop;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// <see cref="JsonlEntryCounter"/> is the JSONL write-back baseline (ISSUE.009/014): it must equal the
/// displayable-entry count of <see cref="ClaudeSessionImportService.ParseJsonlSession"/> and report
/// "no baseline" (null) rather than zero when there is nothing to read (D2.2, perf-diag F1).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class JsonlEntryCounterTests : IDisposable
{
	private const string WorkDir = @"C:\Projects\Counter";

	private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "cmx-entry-counter-" + Guid.NewGuid().ToString("N"));
	private readonly ClaudeSessionImportService _import = new();
	private readonly JsonlEntryCounter _sut;
	private readonly string[] _projectsDirs;

	public JsonlEntryCounterTests()
	{
		_projectsDirs = [Path.Combine(_tempRoot, "default", "projects"), Path.Combine(_tempRoot, "profile_1", "projects")];
		_sut = new JsonlEntryCounter(_import);
	}

	public void Dispose()
	{
		if (Directory.Exists(_tempRoot))
			Directory.Delete(_tempRoot, recursive: true);
	}

	private string WriteJsonl(int dirIndex, string sessionId, params string[] lines)
	{
		var slugDir = Path.Combine(_projectsDirs[dirIndex], Constants.ClaudeSessions.BuildProjectSlug(WorkDir));
		Directory.CreateDirectory(slugDir);
		var path = Path.Combine(slugDir, sessionId + Constants.ClaudeSessions.SessionFileExtension);
		File.WriteAllLines(path, lines, Encoding.UTF8);
		return path;
	}

	private static string Line(string type, string messageJson, string ts) =>
		"{\"type\":\"" + type + "\",\"message\":" + messageJson + ",\"timestamp\":\"" + ts + "\"}";

	[Fact]
	public void Count_EqualsDisplayableEntriesOfFullParse_AcrossMixedLines()
	{
		var path = WriteJsonl(1, "mixed",
			Line("user", """{"role":"user","content":"Hello"}""", "2026-03-10T10:00:00Z"),
			Line("assistant", """{"content":[{"type":"text","text":"Checking."},{"type":"tool_use","name":"Bash","input":{"command":"ls","description":"List"}}]}""", "2026-03-10T10:00:05Z"),
			"{ this is not json",
			Line("user", """{"role":"user","content":[{"type":"tool_result","content":"ok"}]}""", "2026-03-10T10:00:06Z"),
			"""{"type":"summary","summary":"A summary"}""",
			Line("assistant", """{"content":[{"type":"text","text":"   "}]}""", "2026-03-10T10:00:07Z"),
			Line("assistant", """{"content":[{"type":"text","text":"Done."}]}""", "2026-03-10T10:00:08Z"));

		var expected = _import.ParseJsonlSession(path)
			.Count(e => e.Role != Constants.SessionFile.RoleCompaction && !string.IsNullOrWhiteSpace(e.Content));

		var count = _sut.CountDisplayableEntries(WorkDir, "mixed", _projectsDirs);

		Assert.True(expected >= 4);
		Assert.Equal(expected, count);
	}

	[Fact]
	public void Count_NoJsonlFile_ReturnsNull_NotZero() =>
		Assert.Null(_sut.CountDisplayableEntries(WorkDir, "absent", _projectsDirs));

	[Theory]
	[InlineData(WorkDir, null)]
	[InlineData(WorkDir, "")]
	[InlineData("", "some-id")]
	public void Count_MissingIdentity_ReturnsNull(string workingDirectory, string? sessionId) =>
		Assert.Null(_sut.CountDisplayableEntries(workingDirectory, sessionId, _projectsDirs));

	[Fact]
	public void Count_FindsFileInAnyProfileStore()
	{
		WriteJsonl(1, "in-profile", Line("user", """{"role":"user","content":"Hi"}""", "2026-03-10T10:00:00Z"));

		Assert.Equal(1, _sut.CountDisplayableEntries(WorkDir, "in-profile", _projectsDirs));
	}
}
