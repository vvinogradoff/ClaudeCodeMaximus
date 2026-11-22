namespace TessynDesktop.Models;

/// <summary>
/// One row of the GPT tier table (FR.33.14): a GPT model whose id contains <see cref="Match"/> (case-insensitive) has <see cref="Tier"/>. Rows
/// are tried top first.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexModelTierModel
{
	public string Match { get; set; } = string.Empty;

	public int Tier { get; set; }
}
