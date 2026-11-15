namespace TessynDesktop.Models.Remote;

/// <summary>What the Telegram mirror posts to a session's topic (wire form of the daemon's setting).</summary>
/// <remarks>Created by Claude</remarks>
public enum TelegramMirrorModeDto
{
	Full = 0,
	AnswersOnly = 1,
	Off = 2,
}
