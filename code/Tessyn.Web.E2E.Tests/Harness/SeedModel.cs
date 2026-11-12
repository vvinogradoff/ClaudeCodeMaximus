namespace Tessyn.Web.E2E.Tests.Harness;

/// <summary>The ids and names of the seeded tree, so tests can address nodes without searching.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SeedModel
{
	public const string AlphaLabel = "alpha";
	public const string BetaLabel = "beta";
	public const string GroupName = "Backend";
	public const string NestedGroupName = "Deep";
	public const string SupervisorName = "Alpha supervisor";
	public const string WorkerOneName = "Alpha worker one";
	public const string WorkerTwoName = "Alpha worker two";
	public const string FavoriteName = "Alpha favourite";
	public const string BackendName = "Backend api";
	public const string DeepName = "Deep dive";
	public const string BetaName = "Beta plain";
	public const string SearchTargetName = "Beta search target";
	public const string SearchWord = "zebrafinch";
	public const string PullRequestUrl = "https://github.com/acme/widgets/pull/12";
	public const string RepositoryUrl = "https://github.com/acme/widgets";
	public const string AskTitle = "Which database should we use?";
	public const string TodoTitle = "Rotate the staging key";
	public const string AlertTitle = "Disk almost full";

	public string AlphaDirectoryId { get; init; } = string.Empty;
	public string BetaDirectoryId { get; init; } = string.Empty;
	public string GroupId { get; init; } = string.Empty;
	public string NestedGroupId { get; init; } = string.Empty;
	public string SupervisorId { get; init; } = string.Empty;
	public string WorkerOneId { get; init; } = string.Empty;
	public string WorkerTwoId { get; init; } = string.Empty;
	public string FavoriteId { get; init; } = string.Empty;
	public string BackendId { get; init; } = string.Empty;
	public string DeepId { get; init; } = string.Empty;
	public string BetaId { get; init; } = string.Empty;
	public string SearchTargetId { get; init; } = string.Empty;
	public string AskItemId { get; init; } = string.Empty;
	public string TodoItemId { get; init; } = string.Empty;
	public string AlertItemId { get; init; } = string.Empty;
	public string AgentToken { get; init; } = string.Empty;
	public string ClaudeSessionId { get; init; } = string.Empty;
	public string AlphaPath { get; init; } = string.Empty;
}
