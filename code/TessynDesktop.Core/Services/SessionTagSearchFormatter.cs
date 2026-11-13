using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using TessynDesktop.Models.Agent;

namespace TessynDesktop.Services;

/// <summary>
/// Renders a <see cref="SessionTagSearchPageModel"/> as the plain text returned by the <c>search_tags</c> MCP
/// tool (FR.24.15 "Output"). The <c>You:</c> line comes from <see cref="SessionListFormatter"/>; labels are
/// printed as they are, control characters becoming spaces.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionTagSearchFormatter
{
	private readonly SessionListText      _text          = new();
	private readonly SessionListFormatter _listFormatter = new();

	private const string Separator         = "  ";
	private const string IdSeparator       = ", ";
	private const string SessionNoun       = "session";
	private const string SessionsNoun      = "sessions";
	private const string NoTagsInDirectory = "No tags in this directory";
	private const string NoTagsYet         = "No tags yet";
	private const string NoMatchingTags    = "No matching tags";
	private const string SessionsHint      = "Sessions: list_sessions tags=[\"<url or key>\"].";

	public string Format(SessionTagSearchPageModel page)
	{
		var lines = new List<string>();

		if (page.Caller is not null)
			lines.Add(_listFormatter.FormatYouLine(page.Caller));

		if (page.TotalRows == 0)
		{
			lines.Add(FormatEmpty(page));
			lines.Add(FormatHints(page));
			return string.Join("\n", lines);
		}

		lines.Add(FormatHeader(page));

		if (page.Rows.Count == 0)
		{
			lines.Add(string.Format(CultureInfo.InvariantCulture, Constants.Agent.ListPastEndFormat, page.Offset, page.TotalRows));
			lines.Add(FormatHints(page));
			return string.Join("\n", lines);
		}

		lines.AddRange(page.Rows.Select(FormatRow));
		lines.Add(FormatFooter(page));
		lines.Add(FormatHints(page));
		return string.Join("\n", lines);
	}

	private string FormatHeader(SessionTagSearchPageModel page)
	{
		var head = page.Scope == SessionListScope.Project
			? $"Tags in {_text.Sanitize(page.DirectoryLabel ?? string.Empty)} ({_text.Sanitize(Path.TrimEndingDirectorySeparator(page.DirectoryPath ?? string.Empty))}) - {Number(page.TotalRows)} tags"
			: $"Tags in all directories - {Number(page.TotalRows)} tags";

		return page.Query is null ? head : $"{head}, matching \"{_text.Sanitize(page.Query)}\"";
	}

	private string FormatRow(SessionTagSearchRowModel row)
	{
		var sb = new StringBuilder();
		sb.Append(_text.Sanitize(row.Label)).Append(Separator).Append(_text.Sanitize(row.Url)).Append(Separator);
		sb.Append(Number(row.SessionCount)).Append(' ').Append(row.SessionCount == 1 ? SessionNoun : SessionsNoun).Append(": ");
		sb.Append(string.Join(IdSeparator, row.CarrierShortIds));

		var more = row.SessionCount - row.CarrierShortIds.Count;
		if (more > 0)
			sb.Append(" +").Append(Number(more));

		return sb.ToString();
	}

	private static string FormatEmpty(SessionTagSearchPageModel page)
	{
		if (page.TagsInScope > 0)
			return NoMatchingTags;

		return page.Scope == SessionListScope.Project ? NoTagsInDirectory : NoTagsYet;
	}

	private static string FormatFooter(SessionTagSearchPageModel page)
	{
		var first = page.Offset + 1;
		var last  = page.Offset + page.Rows.Count;
		var text  = string.Format(CultureInfo.InvariantCulture, Constants.Agent.ListFooterFormat, first, last, page.TotalRows);
		if (last < page.TotalRows)
			text += string.Format(CultureInfo.InvariantCulture, Constants.Agent.ListNextPageFormat, last);
		return text;
	}

	private static string FormatHints(SessionTagSearchPageModel page) =>
		page.Scope == SessionListScope.Project ? $"{SessionsHint} {Constants.Agent.ListOtherProjectsHint}" : SessionsHint;

	private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
}
