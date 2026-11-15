namespace TessynDesktop.Models.Remote;

/// <summary>Answer of <c>tree.refreshGitOrigins</c>.</summary>
/// <param name="Started">A scan was started; false when one was already running. <c>tree.changed</c> follows when it is done.</param>
/// <remarks>Created by Claude</remarks>
public sealed record TreeRefreshGitOriginsResult(bool Started);
