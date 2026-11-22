using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>Answer of catalog.get: what a turn can be sent with.</summary>
/// <param name="Models">"default" first, then the Anthropic models, the GPT models (Codex's default first) and the Ollama models (FR.33.2).</param>
/// <param name="Efforts">The global effort list (Claude's), kept for older clients; a model's own list is <see cref="ModelOptionDto.Efforts"/>.</param>
/// <param name="Profiles">"default", the stored Claude profiles, then the signed-in ChatGPT profiles (each with its <c>engine</c>).</param>
/// <param name="MediaPreviewHosts">Hosts whose images and videos a client may load without asking (decision A13); an entry like <c>*.example.com</c> covers subdomains.</param>
/// <param name="GptHint">The disabled picker line when the catalog has no GPT models (<c>GPT models: sign in to ChatGPT in Settings &gt; Profiles</c>, FR.33.2); omitted when there are GPT models or Codex was not found.</param>
/// <remarks>Created by Claude</remarks>
public sealed record CatalogResult(
	IReadOnlyList<ModelOptionDto> Models,
	IReadOnlyList<string> Efforts,
	IReadOnlyList<ProfileOptionDto> Profiles,
	IReadOnlyList<string>? MediaPreviewHosts = null,
	string? GptHint = null);
