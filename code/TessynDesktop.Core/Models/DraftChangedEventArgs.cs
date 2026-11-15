using System;

namespace TessynDesktop.Models;

/// <summary>A session's draft was written or deleted.</summary>
/// <param name="FileName">The session file the draft belongs to.</param>
/// <param name="ItemId">The Focus item of a reply draft (FR.23.19); null for the session's composer draft.</param>
/// <remarks>Created by Claude</remarks>
public sealed class DraftChangedEventArgs(string fileName, string? itemId = null) : EventArgs
{
	public string FileName { get; } = fileName;

	public string? ItemId { get; } = itemId;
}
