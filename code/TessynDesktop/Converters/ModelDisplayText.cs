using System;

namespace TessynDesktop.Converters;

/// <summary>
/// Shared presentation-only text transforms for model IDs shown in the command bar (FR.12.22).
/// Never applied to the underlying <c>ClaudeModelInfo.Id</c>/<c>SelectedModelId</c> value used for
/// <c>--model</c>, persistence, or <see cref="TessynDesktop.Services.LocalToolProfileResolver"/>
/// matching — those keep the raw ID including any ":latest" suffix.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal static class ModelDisplayText
{
	private const string LatestSuffix = ":latest";

	/// <summary>Strips a trailing Ollama ":latest" tag (e.g. "qwen3.6:35b-a3b:latest" → "qwen3.6:35b-a3b") for display only.</summary>
	internal static string StripOllamaLatestSuffix(string s) =>
		s.EndsWith(LatestSuffix, StringComparison.Ordinal) ? s[..^LatestSuffix.Length] : s;
}
