using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>One question of a Focus questionnaire as the Remote API shows it (FR.23.38).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record AttentionQuestionDto
{
	/// <summary>1-based, in posting order.</summary>
	public int Number { get; init; }

	public AttentionQuestionType Type { get; init; }

	public string Text { get; init; } = string.Empty;

	/// <summary>Single and Multi only, 2 to 4 labels.</summary>
	public IReadOnlyList<string> Choices { get; init; } = [];

	/// <summary>"yes" or "no", a choice label, or a text; null for Multi and for no recommendation.</summary>
	public string? Recommended { get; init; }

	/// <summary>Multi only: the recommended subset of <see cref="Choices"/>.</summary>
	public IReadOnlyList<string> RecommendedChoices { get; init; } = [];
}
