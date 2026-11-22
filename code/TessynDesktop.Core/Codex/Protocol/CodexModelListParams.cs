namespace TessynDesktop.Codex.Protocol;

/// <summary>Params of <c>model/list</c> (FR.33.2).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexModelListParams
{
	public string? Cursor { get; set; }
	public int? Limit { get; set; }
	public bool? IncludeHidden { get; set; }
}
