using System.Collections.Generic;

namespace TessynDesktop.Models;

/// <summary>One question of a Focus questionnaire as stored in the ledger (FR.23.33).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class AttentionQuestionModel
{
	/// <summary>1-based, in posting order.</summary>
	public int Number { get; set; }

	public AttentionQuestionType Type { get; set; }

	public string Text { get; set; } = string.Empty;

	/// <summary>Single and Multi only, 2 to 4 labels in their stored spelling.</summary>
	public List<string> Choices { get; set; } = [];

	/// <summary>"yes" or "no" for YesNo, a choice label for Single, the text for Text; null for Multi and for no recommendation.</summary>
	public string? Recommended { get; set; }

	/// <summary>Multi only: the recommended subset of <see cref="Choices"/>, in choice order.</summary>
	public List<string> RecommendedChoices { get; set; } = [];
}
