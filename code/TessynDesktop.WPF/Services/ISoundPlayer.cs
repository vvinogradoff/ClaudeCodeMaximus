namespace TessynDesktop.WPF.Services;

/// <summary>Plays a .wav file without blocking. The completion sound goes through it (W1b); the toast and sound package (W10b) may replace the implementation.</summary>
/// <remarks>Created by Claude</remarks>
public interface ISoundPlayer
{
	/// <summary>Starts playing <paramref name="file"/> and returns at once; a missing or unplayable file is logged, never thrown.</summary>
	void Play(string file);
}
