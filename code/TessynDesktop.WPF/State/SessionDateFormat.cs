namespace TessynDesktop.WPF.State;

/// <summary>How a session row shows the date of the last prompt (Settings, Appearance).</summary>
/// <remarks>Created by Claude</remarks>
public enum SessionDateFormat
{
	/// <summary><c>yyyy-MM-dd</c>.</summary>
	Absolute,

	/// <summary>The age of the last prompt: <c>now</c>, <c>5m</c>, <c>3h</c>, <c>9d</c>, then weeks (<c>107w</c>).</summary>
	Relative,
}
