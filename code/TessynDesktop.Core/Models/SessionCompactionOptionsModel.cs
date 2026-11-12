namespace TessynDesktop.Models;

/// <summary>
/// What a compaction run is sent with: the model, effort and profile the caller selected. The Ollama base url, the tool
/// switches and the Local Tool Profile are derived from <see cref="ModelInfo"/> by the service.
/// </summary>
/// <param name="ModelId">Value for <c>--model</c>; null = no flag.</param>
/// <param name="Effort">Value for <c>--effort</c>; null = no flag.</param>
/// <param name="ProfileConfigDir">CLAUDE_CONFIG_DIR of the profile; null = the Default profile.</param>
/// <param name="ModelInfo">Catalog entry of <paramref name="ModelId"/> when known (decides Ollama, tool support, Local Tool Profile).</param>
/// <remarks>Created by Claude</remarks>
public sealed record SessionCompactionOptionsModel(
	string? ModelId,
	string? Effort,
	string? ProfileConfigDir,
	ClaudeModelInfo? ModelInfo);
