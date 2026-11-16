namespace TessynDesktop.WPF.Services;

/// <summary>Which of the four font sizes of the Appearance settings a change applies to.</summary>
/// <remarks>Created by Claude</remarks>
public enum FontTarget
{
	/// <summary>Plain assistant text: system and compaction rows and the streaming row of a running turn.</summary>
	Assistant,

	/// <summary>Rendered (markdown) assistant entries.</summary>
	AssistantMarkdown,

	/// <summary>The user's own entries.</summary>
	User,

	/// <summary>The prompt box.</summary>
	Input,
}
