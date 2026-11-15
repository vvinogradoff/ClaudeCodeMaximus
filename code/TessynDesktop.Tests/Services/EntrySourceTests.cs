using System;
using System.IO;
using System.Linq;
using System.Text;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// The source of a USER entry (scheduled / supervisor / Telegram / web inputs shown apart): the header meta field, its round trip, the legacy
/// "[Scheduled]" prefix, the mapping from <see cref="TurnSource"/> and the proof that a build without this feature still reads the files.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class EntrySourceTests : IDisposable
{
	private readonly string _tempDir;
	private readonly SessionFileService _sut;

	public EntrySourceTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), "cm_test_sources_" + Path.GetRandomFileName());
		Directory.CreateDirectory(_tempDir);
		var settings = new AppSettingsService(_tempDir);
		settings.Settings.SessionFilesRoot = _tempDir;
		_sut = new SessionFileService(settings);
	}

	public void Dispose() => Directory.Delete(_tempDir, recursive: true);

	private string Raw(string fileName) => File.ReadAllText(Path.Combine(_tempDir, fileName), Encoding.UTF8);

	[Theory]
	[InlineData(TurnSource.User, null)]
	[InlineData(TurnSource.Scheduled, "scheduled")]
	[InlineData(TurnSource.Orchestrated, "orchestrated")]
	[InlineData(TurnSource.Telegram, "telegram")]
	[InlineData(TurnSource.Web, "web")]
	public void The_header_value_follows_the_turn_source(TurnSource source, string? expected)
	{
		Assert.Equal(expected, EntrySources.FromTurnSource(source));
	}

	[Theory]
	[InlineData("scheduled")]
	[InlineData("orchestrated")]
	[InlineData("telegram")]
	[InlineData("web")]
	public void A_source_written_on_a_user_entry_round_trips_through_the_file(string source)
	{
		var fileName = _sut.CreateSessionFile();
		_sut.AppendMessage(fileName, Constants.SessionFile.RoleUser, "do the thing", profileName: "work", source: source);

		var entry = Assert.Single(_sut.ReadEntries(fileName));

		Assert.Equal(source, entry.Source);
		Assert.Equal("work", entry.ProfileName);
		Assert.Equal("do the thing", entry.Content);
		Assert.Contains($" source=\"{source}\"", Raw(fileName));
	}

	[Fact]
	public void The_desktop_user_has_no_source_and_the_header_has_no_source_field()
	{
		var fileName = _sut.CreateSessionFile();
		_sut.AppendMessage(fileName, Constants.SessionFile.RoleUser, "typed by hand", profileName: "work", source: EntrySources.FromTurnSource(TurnSource.User));

		Assert.Null(Assert.Single(_sut.ReadEntries(fileName)).Source);
		Assert.DoesNotContain("source=", Raw(fileName));
	}

	[Fact]
	public void The_appended_event_carries_the_source()
	{
		var fileName = _sut.CreateSessionFile();
		SessionEntryAppendedEventArgs? received = null;
		_sut.EntryAppended += (_, e) => received = e;

		_sut.AppendMessage(fileName, Constants.SessionFile.RoleUser, "wake up", source: EntrySources.Scheduled);

		Assert.Equal(EntrySources.Scheduled, received!.Entry.Source);
	}

	[Fact]
	public void A_source_survives_a_rewrite_of_the_file()
	{
		var fileName = _sut.CreateSessionFile();
		_sut.AppendMessage(fileName, Constants.SessionFile.RoleUser, "from the supervisor", source: EntrySources.Orchestrated);
		_sut.AppendMessage(fileName, Constants.SessionFile.RoleAssistant, "done", modelId: "opus");

		_sut.WriteSessionFile(fileName, _sut.ReadEntries(fileName));
		var entries = _sut.ReadEntries(fileName);

		Assert.Equal(EntrySources.Orchestrated, entries[0].Source);
		Assert.Null(entries[1].Source);
	}

	[Fact]
	public void Only_user_entries_carry_a_source()
	{
		var lines = new[]
		{
			"[2026-11-15T10:00:00Z] ASSISTANT model=\"opus\" source=\"scheduled\"",
			"reply",
			"",
			"[2026-11-15T10:01:00Z] SYSTEM source=\"web\"",
			"note",
		};

		var entries = SessionFileService.ParseEntries(lines);

		Assert.All(entries, e => Assert.Null(e.Source));
	}

	// ── legacy: a scheduled prompt written before the source was recorded ───────────────────

	[Fact]
	public void An_unmarked_user_entry_that_starts_with_the_scheduled_prefix_is_resolved_as_scheduled_on_read_only()
	{
		var lines = new[]
		{
			"[2026-11-15T10:00:00Z] USER profile=\"work\"",
			$"{Constants.Agent.ScheduledTurnSystemPrefix} Nightly check",
			"run the checks",
			"",
			"[2026-11-15T10:01:00Z] USER",
			"I mention [Scheduled] in the middle",
		};

		var entries = SessionFileService.ParseEntries(lines);

		// The parsed model holds what the file records (nothing); the DTO resolves the legacy prefix.
		Assert.Null(entries[0].Source);
		Assert.Equal(EntrySources.Scheduled, entries[0].ToDto(0, 1000).Source);
		Assert.Null(entries[1].ToDto(1, 1000).Source);
	}

	[Fact]
	public void The_prefix_is_resolved_from_the_full_content_even_when_the_dto_cuts_it()
	{
		var legacy = new SessionEntryModel { Timestamp = DateTimeOffset.UtcNow, Role = "USER", Content = Constants.Agent.ScheduledTurnSystemPrefix + " " + new string('x', 500) };

		Assert.Equal(EntrySources.Scheduled, legacy.ToDto(0, 10).Source);
	}

	[Fact]
	public void Rewriting_a_file_never_writes_a_source_for_a_legacy_prefixed_entry()
	{
		var fileName = _sut.CreateSessionFile();
		_sut.AppendMessage(fileName, Constants.SessionFile.RoleUser, $"{Constants.Agent.ScheduledTurnSystemPrefix} typed or legacy");

		_sut.WriteSessionFile(fileName, _sut.ReadEntries(fileName));

		Assert.DoesNotContain("source=", Raw(fileName));
		Assert.Null(_sut.ReadEntries(fileName)[0].Source);
	}

	[Fact]
	public void A_recorded_source_wins_over_the_prefix_and_the_prefix_means_nothing_outside_user_entries()
	{
		var lines = new[]
		{
			"[2026-11-15T10:00:00Z] USER source=\"telegram\"",
			$"{Constants.Agent.ScheduledTurnSystemPrefix} pasted by the user",
			"",
			"[2026-11-15T10:01:00Z] ASSISTANT",
			$"{Constants.Agent.ScheduledTurnSystemPrefix} quoted",
		};

		var entries = SessionFileService.ParseEntries(lines);

		Assert.Equal(EntrySources.Telegram, entries[0].Source);
		Assert.Null(entries[1].Source);
	}

	// ── rollback safety: a build without the feature reads the new files ────────────────────

	[Fact]
	public void The_header_keeps_the_existing_fields_and_adds_source_last_as_a_plain_key_value_pair()
	{
		var fileName = _sut.CreateSessionFile();
		_sut.AppendMessage(fileName, Constants.SessionFile.RoleUser, "x", profileName: "work", source: "scheduled");

		var header = Raw(fileName).TrimStart('﻿').Split('\n')[0].TrimEnd('\r');

		Assert.Matches(@"^\[\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z\] USER profile=""work"" source=""scheduled""$", header);
	}

	[Fact]
	public void The_older_parser_logic_still_reads_every_field_but_the_new_one()
	{
		// What a build from before this change does with a header: split off the role, read the key="value" pairs it knows, ignore the rest.
		// (The shared helpers are the ones such a build contains: TryParseHeader and ParseMetadata did not change.)
		var line = "[2026-11-15T10:00:00Z] USER profile=\"work\" source=\"scheduled\"";

		Assert.True(SessionFileService.TryParseHeader(line, out var timestamp, out var role, out var rawMeta));
		var meta = SessionFileService.ParseMetadata(rawMeta);

		Assert.Equal(new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.Zero), timestamp);
		Assert.Equal("USER", role);
		Assert.Equal("work", meta["profile"]);
		Assert.False(meta.ContainsKey("model"));
		Assert.False(meta.ContainsKey("effort"));
		Assert.Equal("scheduled", meta["source"]);   // present, but nothing in an older build asks for it
	}

	// ── the wire DTO ────────────────────────────────────────────────────────────────────────────

	[Fact]
	public void The_dto_carries_the_source_and_omits_it_when_there_is_none()
	{
		var scheduled = new SessionEntryModel { Timestamp = DateTimeOffset.UtcNow, Role = "USER", Content = "tick", Source = EntrySources.Scheduled };
		var own = new SessionEntryModel { Timestamp = DateTimeOffset.UtcNow, Role = "USER", Content = "hi" };

		Assert.Equal("scheduled", scheduled.ToDto(0, 1000).Source);
		Assert.Null(own.ToDto(1, 1000).Source);
	}
}
