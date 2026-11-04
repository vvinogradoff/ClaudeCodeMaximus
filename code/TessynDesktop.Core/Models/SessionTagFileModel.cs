using System.Collections.Generic;

namespace TessynDesktop.Models;

/// <summary>The declared tags of one session as persisted in <c>tags\&lt;NodeId&gt;.json</c> (FR.24.5). Name-derived tags are never stored.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionTagFileModel
{
	public List<SessionTagModel> Tags { get; set; } = [];
}
