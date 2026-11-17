namespace TessynDesktop.Services;

/// <summary>
/// The mode of one session's tag area (FR.24.16): "mini" (false, one line) or "full" (true, up to ten lines). It is never persisted and never
/// shared: a session starts collapsed every time it is opened, so the owner calls <see cref="Reset"/> when the shown session changes. Switching
/// between the Session and Focus views is not a session change and keeps the mode.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TagAreaMode
{
	public static bool DefaultExpanded => false;

	public bool IsExpanded { get; private set; } = DefaultExpanded;

	/// <summary>Raised after the mode changed.</summary>
	public event System.Action? Changed;

	/// <summary>Sets the mode; does nothing when it is already that value.</summary>
	public void Set(bool expanded)
	{
		if (IsExpanded == expanded)
			return;

		IsExpanded = expanded;
		Changed?.Invoke();
	}

	/// <summary>Back to the default (collapsed): a session was opened.</summary>
	public void Reset() => Set(DefaultExpanded);
}
