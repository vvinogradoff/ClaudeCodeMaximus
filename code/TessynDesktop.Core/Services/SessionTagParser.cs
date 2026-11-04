using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Turns one <c>tag_session</c> entry into a tag (FR.24.1, FR.24.4) and finds Linear keys in a session name
/// (FR.24.2). Pure: the Linear workspace map is passed in, and nothing is read from conversation text.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionTagParser
{
	private static readonly Regex NameToken = new(Constants.SessionTags.NameTokenPattern, RegexOptions.CultureInvariant);
	private static readonly Regex LinearKey = new(Constants.SessionTags.LinearKeyPattern, RegexOptions.CultureInvariant);
	private static readonly Regex ShortPullRequest = new(Constants.SessionTags.ShortPullRequestPattern, RegexOptions.CultureInvariant);
	private static readonly Regex ShortRepository = new(Constants.SessionTags.ShortRepositoryPattern, RegexOptions.CultureInvariant);

	/// <summary>
	/// Parses one entry (trimmed first). <paramref name="workspaces"/> maps a Linear team key to its workspace slug
	/// and is used to resolve short keys; a full Linear URL instead reports the pair it teaches.
	/// </summary>
	public SessionTagParseResultModel Parse(string? entry, IReadOnlyDictionary<string, string> workspaces)
	{
		var text = entry?.Trim() ?? string.Empty;
		if (text.Length == 0)
			return Result(SessionTagParseOutcome.Empty);

		if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
			return ParseUrl(text);

		var key = LinearKey.Match(text);
		if (key.Success)
			return ParseShortLinear(key, workspaces);

		var pr = ShortPullRequest.Match(text);
		if (pr.Success)
			return ParsePullRequest(pr.Groups[1].Value, pr.Groups[2].Value, pr.Groups[3].Value);

		var repo = ShortRepository.Match(text);
		if (repo.Success)
			return ParseRepository(repo.Groups[1].Value, repo.Groups[2].Value);

		return Result(SessionTagParseOutcome.Invalid);
	}

	/// <summary>Distinct upper-case <c>KEY-123</c> tokens of a session name, in order of appearance (FR.24.2).</summary>
	public IReadOnlyList<string> FindNameKeys(string? sessionName)
	{
		if (string.IsNullOrEmpty(sessionName))
			return [];

		return NameToken.Matches(sessionName)
			.Select(m => m.Value)
			.Distinct(StringComparer.Ordinal)
			.ToList();
	}

	/// <summary>The Linear tag for an issue key (already upper-case) in a workspace.</summary>
	public SessionTagModel BuildLinearTag(string issueKey, string workspace) => new()
	{
		Kind  = SessionTagKind.LinearIssue,
		Key   = issueKey,
		Label = issueKey,
		Url   = string.Format(Constants.SessionTags.LinearIssueUrlFormat, workspace, issueKey),
	};

	private SessionTagParseResultModel ParseShortLinear(Match key, IReadOnlyDictionary<string, string> workspaces)
	{
		var team     = key.Groups[1].Value.ToUpperInvariant();
		var issueKey = $"{team}-{key.Groups[2].Value}";
		if (!workspaces.TryGetValue(team, out var workspace))
			return new SessionTagParseResultModel { Outcome = SessionTagParseOutcome.UnknownTeam, UnknownTeam = team, UnknownIssueKey = issueKey };

		return new SessionTagParseResultModel { Outcome = SessionTagParseOutcome.Valid, Tag = BuildLinearTag(issueKey, workspace) };
	}

	private SessionTagParseResultModel ParseUrl(string text)
	{
		if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.UserInfo.Length > 0)
			return Result(SessionTagParseOutcome.Invalid);

		var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

		if (uri.Host == Constants.SessionTags.LinearHost)
			return ParseLinearUrl(segments);
		if (uri.Host == Constants.SessionTags.GitHubHost)
			return ParseGitHubUrl(segments);

		return Result(SessionTagParseOutcome.Invalid);
	}

	private SessionTagParseResultModel ParseLinearUrl(string[] segments)
	{
		if (segments.Length < 3 || !string.Equals(segments[1], Constants.SessionTags.LinearIssueSegment, StringComparison.OrdinalIgnoreCase))
			return Result(SessionTagParseOutcome.Invalid);

		var key = LinearKey.Match(segments[2]);
		if (!key.Success)
			return Result(SessionTagParseOutcome.Invalid);

		var team     = key.Groups[1].Value.ToUpperInvariant();
		var workspace = segments[0];
		return new SessionTagParseResultModel
		{
			Outcome          = SessionTagParseOutcome.Valid,
			Tag              = BuildLinearTag($"{team}-{key.Groups[2].Value}", workspace),
			LearnedTeam      = team,
			LearnedWorkspace = workspace,
		};
	}

	private SessionTagParseResultModel ParseGitHubUrl(string[] segments)
	{
		if (segments.Length < 2)
			return Result(SessionTagParseOutcome.Invalid);

		var owner = segments[0];
		var repo  = segments[1];
		if (repo.EndsWith(Constants.SessionTags.GitDirectorySuffix, StringComparison.OrdinalIgnoreCase) && segments.Length == 2)
			repo = repo[..^Constants.SessionTags.GitDirectorySuffix.Length];

		if (segments.Length == 2)
			return ParseRepository(owner, repo);

		if (segments.Length >= 4 && string.Equals(segments[2], Constants.SessionTags.GitHubPullSegment, StringComparison.OrdinalIgnoreCase))
			return ParsePullRequest(owner, repo, segments[3]);

		return Result(SessionTagParseOutcome.Invalid);
	}

	private SessionTagParseResultModel ParsePullRequest(string owner, string repo, string number)
	{
		if (!IsOwnerAndRepo(owner, repo) || !int.TryParse(number, out var n) || n <= 0)
			return Result(SessionTagParseOutcome.Invalid);

		var tag = new SessionTagModel
		{
			Kind  = SessionTagKind.GitHubPullRequest,
			Key   = string.Format(Constants.SessionTags.PullRequestKeyFormat, owner, repo, n),
			Label = string.Format(Constants.SessionTags.PullRequestLabelFormat, owner, repo, n),
			Url   = string.Format(Constants.SessionTags.GitHubPullRequestUrlFormat, owner, repo, n),
		};
		return new SessionTagParseResultModel { Outcome = SessionTagParseOutcome.Valid, Tag = tag };
	}

	private SessionTagParseResultModel ParseRepository(string owner, string repo)
	{
		if (!IsOwnerAndRepo(owner, repo))
			return Result(SessionTagParseOutcome.Invalid);

		var tag = new SessionTagModel
		{
			Kind  = SessionTagKind.GitHubRepository,
			Key   = string.Format(Constants.SessionTags.RepositoryKeyFormat, owner, repo),
			Label = repo,
			Url   = string.Format(Constants.SessionTags.GitHubRepositoryUrlFormat, owner, repo),
		};
		return new SessionTagParseResultModel { Outcome = SessionTagParseOutcome.Valid, Tag = tag };
	}

	/// <summary>Owner and repo must look like GitHub names; a repo made only of dots would form a path traversal.</summary>
	private static bool IsOwnerAndRepo(string owner, string repo) =>
		repo.Trim('.').Length > 0
		&& ShortRepository.IsMatch(string.Format(Constants.SessionTags.RepositoryKeyFormat, owner, repo));

	private static SessionTagParseResultModel Result(SessionTagParseOutcome outcome) => new() { Outcome = outcome };
}
