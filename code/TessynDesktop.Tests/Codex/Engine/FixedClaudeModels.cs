using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Codex.Engine;

/// <summary>The Claude catalog of the phase 3 tests: Fable, Opus, Sonnet, Haiku and one local Ollama model.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class FixedClaudeModels : IClaudeModelService
{
	public const string Fable  = "claude-fable-5";
	public const string Opus   = "claude-opus-5-5";
	public const string Sonnet = "claude-sonnet-5-5";
	public const string Haiku  = "claude-haiku-4-5-20251001";
	public const string Local  = "qwen3:8b";

	public IReadOnlyList<ClaudeModelInfo> Models { get; set; } =
	[
		new(Fable, "fable", "Fable 5"),
		new(Opus, "opus", "Opus 5.5"),
		new(Sonnet, "sonnet", "Sonnet 5.5"),
		new(Haiku, "haiku", "Haiku 4.5"),
		new(Local, string.Empty, "Qwen3 8B", ModelProvider.Ollama),
	];

	public IReadOnlyList<ClaudeModelInfo> GetCachedModels() => Models;

	public Task EnsureModelsLoadedAsync() => Task.CompletedTask;

	public event EventHandler? ModelsUpdated { add { } remove { } }
}
