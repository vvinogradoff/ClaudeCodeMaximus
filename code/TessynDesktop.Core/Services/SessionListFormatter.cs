using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using TessynDesktop.Models.Agent;

namespace TessynDesktop.Services;

/// <summary>
/// Renders a <see cref="SessionListPageModel"/> as the plain text returned by the
/// <c>list_sessions</c> MCP tool (FR.15.13 "Output"). The listing's own words are ASCII; session,
/// group and directory names are printed as they are.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionListFormatter
{
	private readonly SessionListText _text = new();

	private const string MissingAge        = "-";
	private const string NowAge            = "now";
	private const string MinutesUnit       = "m";
	private const string HoursUnit         = "h";
	private const string DaysUnit          = "d";
	private const string WeeksUnit         = "w";
	private const string MonthsUnit        = "mo";
	private const string YearsUnit         = "y";
	private const string YouPrefix         = "You: ";
	private const string RunningFlag       = "running";
	private const string FavoriteFlag      = "fav";
	private const string NoResumeFlag      = "no-resume";
	private const string WorkerNoun        = "worker";
	private const string WorkersNoun       = "workers";
	private const string RunningOpen       = " (";
	private const string RunningSuffix     = " running)";
	private const string WorkerOfYouFlag   = "worker of you";
	private const string WorkerOfPrefix    = "worker of ";
	private const string CallerFlag        = "(you)";
	private const string Separator         = "  ";
	private const string IndentUnit        = "  ";
	private const string PathOpen          = " (";
	private const string NoMatchText       = "No sessions match.";
	private const string WorkersHintPrefix = "Workers: scope=children nodeId=";
	private const string NarrowHint        = "Narrow: query=, running=, activeWithinHours=.";

	private const int SecondsPerMinute = 60;
	private const int MinutesPerHour   = 60;
	private const int HoursPerDay      = 24;
	private const int DaysPerWeek      = 7;
	private const int DaysPerMonth     = 30;
	private const int DaysPerYear      = 365;
	private const int MaxDaysAsDays    = 14;
	private const int MaxWeeks         = 8;

	public string Format(SessionListPageModel page, DateTimeOffset nowUtc)
	{
		var lines = new List<string>();

		if (page.Caller is not null)
			lines.Add(FormatYouLine(page.Caller));

		if (page.Rows.Count == 0)
		{
			if (page.Sections.Count > 0)
				lines.Add(FormatSectionHeader(page.Sections[0]));
			lines.Add(page.TotalRows > 0 ? FormatPastEnd(page) : NoMatchText);
			lines.Add(FormatHints(page));
			return string.Join("\n", lines);
		}

		var currentSection = -1;
		foreach (var row in page.Rows)
		{
			if (row.SectionIndex != currentSection)
			{
				currentSection = row.SectionIndex;
				if (currentSection >= 0 && currentSection < page.Sections.Count)
					lines.Add(FormatSectionHeader(page.Sections[currentSection]));
			}
			lines.Add(FormatRow(row, nowUtc));
		}

		lines.Add(FormatFooter(page));
		lines.Add(FormatHints(page));
		return string.Join("\n", lines);
	}

	public string FormatYouLine(SessionListCallerModel caller)
	{
		var id    = $"{caller.ShortId} \"{_text.FormatName(caller.Name)}\"";
		var label = _text.Sanitize(caller.DirectoryLabel);

		if (caller.OrchestrationDepth == 0)
			return $"{YouPrefix}{id} - top-level session in {label}";

		var supervisor = caller.SupervisorShortId is null
			? string.Empty
			: $" of {caller.SupervisorShortId} \"{_text.FormatName(caller.SupervisorName ?? string.Empty)}\"";

		return $"{YouPrefix}{id} - worker (depth {Number(caller.OrchestrationDepth)}){supervisor} in {label}";
	}

	private string FormatSectionHeader(SessionListSectionModel section)
	{
		if (section.Kind == SessionListSectionKind.Workers)
			return $"Workers of {section.RootShortId} \"{_text.FormatName(section.RootName ?? string.Empty)}\" - {Number(section.DirectCount)} direct, {Number(section.SubtreeCount)} in subtree";

		var path = Path.TrimEndingDirectorySeparator(section.DirectoryPath);
		return $"{_text.Sanitize(section.DirectoryLabel)}{PathOpen}{_text.Sanitize(path)}) - {Number(section.TopLevelCount)} top-level sessions, {Number(section.WorkerCount)} workers";
	}

	private string FormatRow(SessionListRowModel row, DateTimeOffset nowUtc)
	{
		var sb = new StringBuilder();
		for (var i = 0; i < row.IndentLevel; i++)
			sb.Append(IndentUnit);

		sb.Append(row.ShortId).Append(Separator).Append(_text.FormatName(row.Name)).Append(Separator).Append(FormatAge(row.LastActivityUtc, nowUtc));

		if (row.IsRunning)
			sb.Append(Separator).Append(RunningFlag);
		if (row.IsFavorite)
			sb.Append(Separator).Append(FavoriteFlag);
		if (!row.IsResumable)
			sb.Append(Separator).Append(NoResumeFlag);
		if (row.WorkerCount > 0)
		{
			var noun = row.WorkerCount == 1 ? WorkerNoun : WorkersNoun;
			sb.Append(Separator).Append(Number(row.WorkerCount)).Append(' ').Append(noun);
			if (row.RunningWorkerCount > 0)
				sb.Append(RunningOpen).Append(Number(row.RunningWorkerCount)).Append(RunningSuffix);
		}
		if (row.IsWorkerOfCaller)
			sb.Append(Separator).Append(WorkerOfYouFlag);
		else if (row.WorkerOfShortId is not null)
			sb.Append(Separator).Append(WorkerOfPrefix).Append(row.WorkerOfShortId);
		if (row.OtherDirectoryLabel is not null)
			sb.Append(Separator).Append('[').Append(_text.Sanitize(row.OtherDirectoryLabel)).Append(']');
		if (row.IsCaller)
			sb.Append(Separator).Append(CallerFlag);

		return sb.ToString();
	}

	private static string FormatAge(DateTimeOffset? lastActivityUtc, DateTimeOffset nowUtc)
	{
		if (lastActivityUtc is null)
			return MissingAge;

		var elapsed = nowUtc - lastActivityUtc.Value;
		if (elapsed.TotalSeconds < SecondsPerMinute)
			return NowAge;
		if (elapsed.TotalMinutes < MinutesPerHour)
			return Number((long)elapsed.TotalMinutes) + MinutesUnit;
		if (elapsed.TotalHours < HoursPerDay)
			return Number((long)elapsed.TotalHours) + HoursUnit;

		var days = (long)elapsed.TotalDays;
		if (days < MaxDaysAsDays)
			return Number(days) + DaysUnit;
		if (days < MaxWeeks * DaysPerWeek)
			return Number(days / DaysPerWeek) + WeeksUnit;
		if (days < DaysPerYear)
			return Number(days / DaysPerMonth) + MonthsUnit;
		return Number(days / DaysPerYear) + YearsUnit;
	}

	private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

	private static string FormatFooter(SessionListPageModel page)
	{
		var first = page.Offset + 1;
		var last  = page.Offset + page.Rows.Count;
		var text  = string.Format(CultureInfo.InvariantCulture, Constants.Agent.ListFooterFormat, first, last, page.TotalRows);
		if (last < page.TotalRows)
			text += string.Format(CultureInfo.InvariantCulture, Constants.Agent.ListNextPageFormat, last);
		return text;
	}

	private static string FormatPastEnd(SessionListPageModel page) =>
		string.Format(CultureInfo.InvariantCulture, Constants.Agent.ListPastEndFormat, page.Offset, page.TotalRows);

	private static string FormatHints(SessionListPageModel page)
	{
		var hints = new List<string>();
		if (page.Scope == SessionListScope.Project)
			hints.Add(Constants.Agent.ListOtherProjectsHint);
		if (page.WorkersHintShortId is not null)
			hints.Add($"{WorkersHintPrefix}{page.WorkersHintShortId}.");
		hints.Add(NarrowHint);
		return string.Join(" ", hints);
	}
}
