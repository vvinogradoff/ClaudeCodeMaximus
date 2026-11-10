using System.Collections.Generic;

namespace TessynDesktop.Models;

/// <summary>Managed thread stacks of this process: the full text, and the UI thread's frames on their own.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record ThreadStackCaptureModel(
	bool Success,
	string Text,
	IReadOnlyList<string> UiThreadFrames);
