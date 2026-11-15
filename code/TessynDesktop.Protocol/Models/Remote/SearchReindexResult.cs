namespace TessynDesktop.Models.Remote;

/// <summary>Result of <c>search.reindex</c>.</summary>
/// <param name="Started">A scan was started; false when one is already running. <c>index.changed</c> follows when it indexed something.</param>
/// <remarks>Created by Claude</remarks>
public sealed record SearchReindexResult(bool Started);
