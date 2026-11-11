using System.Collections.Generic;

namespace TessynDesktop.Models;

/// <summary>One distinct Session Tag in the Session Tag Index (FR.24.13) and the top-level sessions carrying it.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionTagUsageModel
{
	public SessionTagModel Tag { get; set; } = new();   // representative copy (FR.24.13)
	public List<string> NodeIds { get; set; } = [];      // distinct top-level session NodeIds in scope (Worker Sessions rolled up)
}
