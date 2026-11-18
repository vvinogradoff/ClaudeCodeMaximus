using Microsoft.Extensions.Logging;

namespace TessynDesktop.WPF.Services;

/// <summary>Plays nothing and says so in the log: a test window must never make noise (a sound is the only thing that cannot be hidden off-screen).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SilentSoundPlayer : ISoundPlayer
{
	private readonly ILogger<SilentSoundPlayer> _logger;

	public SilentSoundPlayer(ILogger<SilentSoundPlayer> logger)
	{
		_logger = logger;
	}

	public void Play(string file) => _logger.LogInformation("Focus item sound (silent in a test window): {File}", file);
}
