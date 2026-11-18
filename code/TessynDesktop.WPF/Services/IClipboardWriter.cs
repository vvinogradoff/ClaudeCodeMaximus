namespace TessynDesktop.WPF.Services;

/// <summary>Puts text on the system clipboard. The transcript page has no clipboard access of its own (no host objects, no permissions): its copy commands go through the host.</summary>
/// <remarks>Created by Claude</remarks>
public interface IClipboardWriter
{
	/// <summary>Replaces the clipboard content with <paramref name="text"/>; false when the clipboard stayed busy.</summary>
	bool TrySetText(string text);
}
