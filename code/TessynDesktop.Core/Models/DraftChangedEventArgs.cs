using System;

namespace TessynDesktop.Models;

/// <summary>A session's draft was written or deleted.</summary>
/// <param name="FileName">The session file the draft belongs to.</param>
/// <remarks>Created by Claude</remarks>
public sealed class DraftChangedEventArgs(string fileName) : EventArgs
{
	public string FileName { get; } = fileName;
}
