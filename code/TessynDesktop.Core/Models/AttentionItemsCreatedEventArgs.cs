using System;
using System.Collections.Generic;

namespace TessynDesktop.Models;

/// <summary>Raised when a post created new Attention Items (FR.22.1): updates and closes never raise it.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class AttentionItemsCreatedEventArgs(string nodeId, IReadOnlyList<AttentionItemModel> items) : EventArgs
{
	public string NodeId { get; } = nodeId;

	/// <summary>The created items, in post order (copies).</summary>
	public IReadOnlyList<AttentionItemModel> Items { get; } = items;
}
