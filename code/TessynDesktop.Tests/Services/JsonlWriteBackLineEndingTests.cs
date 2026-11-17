using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// The duplicate final answer: the Core writes a turn's ASSISTANT entry to the .txt, then the JSONL write-back planner meets the same
/// entry in Claude's JSONL one debounce later and must recognise it. The .txt reader rebuilds a multi-line entry with
/// <c>Environment.NewLine</c> (CRLF on Windows) while the JSONL text uses LF, so a plain string comparison never matched a multi-line
/// entry and every multi-line answer (and prompt) was appended a second time. These tests run the real append, read and JSONL-parse paths.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class JsonlWriteBackLineEndingTests : IDisposable
{
	private const string MultiLineAnswer = "Here is the result.\n\n- first point\n- second point\n\nDone.";
	private const string MultiLinePrompt = "Please check:\n1. the focus panel\n2. the tree";

	private readonly string _dir;
	private readonly SessionFileService _files;
	private readonly JsonlWriteBackPlanner _planner = new();

	public JsonlWriteBackLineEndingTests()
	{
		_dir = Path.Combine(Path.GetTempPath(), "cm_wb_" + Path.GetRandomFileName());
		Directory.CreateDirectory(_dir);
		var settings = new AppSettingsService(_dir);
		settings.Settings.SessionFilesRoot = _dir;
		_files = new SessionFileService(settings);
	}

	public void Dispose() => Directory.Delete(_dir, recursive: true);

	private IReadOnlyList<SessionEntryModel> ParseJsonl(params (string role, string text)[] messages)
	{
		var lines = new List<string>();
		foreach (var (role, text) in messages)
		{
			var timestamp = DateTimeOffset.UtcNow.ToString("o");
			lines.Add(role == "user"
				? JsonSerializer.Serialize(new { type = "user", timestamp, message = new { role = "user", content = text } })
				: JsonSerializer.Serialize(new { type = "assistant", timestamp, message = new { role = "assistant", content = new[] { new { type = "text", text } } } }));
		}

		var path = Path.Combine(_dir, Guid.NewGuid() + ".jsonl");
		File.WriteAllLines(path, lines, Encoding.UTF8);
		return new ClaudeSessionImportService().ParseJsonlSession(path);
	}

	[Fact]
	public void MultiLineAnswerWrittenByCore_IsNotWrittenBackFromJsonl()
	{
		var fileName = _files.CreateSessionFile();
		_files.AppendMessage(fileName, Constants.SessionFile.RoleUser, MultiLinePrompt);
		_files.AppendMessage(fileName, Constants.SessionFile.RoleAssistant, MultiLineAnswer, modelId: "claude-opus-5-5", effort: "default");

		var jsonl = ParseJsonl(("user", MultiLinePrompt), ("assistant", MultiLineAnswer));
		var tail  = _files.ReadEntries(fileName).TakeLast(Constants.SessionFile.WriteBackTailWindow).ToList();

		var plan = _planner.Plan(jsonl, 0, tail, isHeadlessTurnActive: false);

		Assert.Empty(plan.EntriesToWrite);
		Assert.Equal(jsonl.Count, plan.NewBaselineCount);
	}

	[Fact]
	public void MultiLineAnswer_WrittenBackAfterTheCoreWroteIt_LeavesOneCopyInTheFile()
	{
		// The watcher's own write path: plan, then AppendMessage every planned entry; run twice (a second debounce pass changes nothing).
		var fileName = _files.CreateSessionFile();
		_files.AppendMessage(fileName, Constants.SessionFile.RoleAssistant, MultiLineAnswer, modelId: "claude-opus-5-5", effort: "default");
		var jsonl = ParseJsonl(("assistant", MultiLineAnswer));

		var baseline = 0;
		for (var pass = 0; pass < 2; pass++)
		{
			var tail = _files.ReadEntries(fileName).TakeLast(Constants.SessionFile.WriteBackTailWindow).ToList();
			var plan = _planner.Plan(jsonl, baseline, tail, isHeadlessTurnActive: false);
			baseline = plan.NewBaselineCount;
			foreach (var entry in plan.EntriesToWrite)
				_files.AppendMessage(fileName, entry.Role, entry.Content);
		}

		var assistants = _files.ReadEntries(fileName).Where(e => e.Role == Constants.SessionFile.RoleAssistant).ToList();
		Assert.Single(assistants);
	}

	[Fact]
	public void ExternalMultiLineTurn_NotInTxt_IsStillWrittenOnce()
	{
		var fileName = _files.CreateSessionFile();
		_files.AppendMessage(fileName, Constants.SessionFile.RoleUser, "older");
		var jsonl = ParseJsonl(("user", "older"), ("user", MultiLinePrompt), ("assistant", MultiLineAnswer));
		var tail  = _files.ReadEntries(fileName).ToList();

		var plan = _planner.Plan(jsonl, 1, tail, isHeadlessTurnActive: false);

		Assert.Equal([MultiLinePrompt, MultiLineAnswer], plan.EntriesToWrite.Select(e => e.Content));
	}

	[Fact]
	public void Planner_TreatsCrLfAndLfAsTheSameContent()
	{
		var tail  = new List<SessionEntryModel> { new() { Timestamp = DateTimeOffset.UtcNow, Role = "ASSISTANT", Content = "a\r\nb\r\n\r\nc" } };
		var jsonl = new List<SessionEntryModel> { new() { Timestamp = DateTimeOffset.UtcNow, Role = "ASSISTANT", Content = "a\nb\n\nc" } };

		var plan = _planner.Plan(jsonl, 0, tail, isHeadlessTurnActive: false);

		Assert.Empty(plan.EntriesToWrite);
	}

	[Fact]
	public void Planner_StillWritesAGenuinelyDifferentMultiLineEntry()
	{
		var tail  = new List<SessionEntryModel> { new() { Timestamp = DateTimeOffset.UtcNow, Role = "ASSISTANT", Content = "a\r\nb" } };
		var jsonl = new List<SessionEntryModel> { new() { Timestamp = DateTimeOffset.UtcNow, Role = "ASSISTANT", Content = "a\nc" } };

		var plan = _planner.Plan(jsonl, 0, tail, isHeadlessTurnActive: false);

		Assert.Single(plan.EntriesToWrite);
	}
}
