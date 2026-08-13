namespace TessynDesktop.Models;

/// <summary>
/// Parameters for spawning a Streaming Session Mode process (FR.19.2). <see cref="SessionId"/>
/// is only consulted the first time a process is spawned for a node (via <c>--resume</c>, so a
/// session already resumed under One-shot Mode continues its history when switched to streaming);
/// it has no effect on later turns sent to an already-live process.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed record StreamingSpawnParams(
	string WorkingDirectory,
	string ClaudePath,
	string? SessionId,
	string? ProfileConfigDir,
	string? Model,
	string? Effort,
	string? McpConfigPath,
	string? OllamaBaseUrl,
	bool DisableTools,
	bool Chrome);
