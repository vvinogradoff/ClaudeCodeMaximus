namespace TessynDesktop.WPF.State;

/// <summary>How a session row shows the date of the last prompt (Settings, Appearance).</summary>
/// <remarks>Created by Claude</remarks>
public enum SessionDateFormat
{
	/// <summary><c>yyyy-MM-dd HH:mm</c>, as the Avalonia client.</summary>
	Absolute,

	/// <summary>The clock time today, otherwise <c>MMM d</c>.</summary>
	Relative,
}
