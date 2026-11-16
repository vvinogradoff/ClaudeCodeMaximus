namespace TessynDesktop.Models;

/// <summary>What an input-navigation key does (FR.2.6).</summary>
/// <remarks>Created by Claude</remarks>
public enum InputKeyActionKind
{
	/// <summary>Not ours: leave the key alone.</summary>
	None,

	/// <summary>Select the previous/next input.</summary>
	Navigate,

	/// <summary>Scroll the transcript.</summary>
	Scroll,

	/// <summary>Clear the selection.</summary>
	Clear,
}
