using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace TessynDesktop.WPF.Services;

/// <summary>Plays .wav files through the Win32 <c>PlaySound</c> of winmm.dll (asynchronously), as the desktop host does.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class WinmmSoundPlayer : ISoundPlayer
{
	private const uint SndAsync = 0x0001;
	private const uint SndFilename = 0x00020000;

	private readonly ILogger<WinmmSoundPlayer> _logger;

	public WinmmSoundPlayer(ILogger<WinmmSoundPlayer> logger)
	{
		_logger = logger;
	}

	[DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool PlaySound(string? sound, IntPtr module, uint flags);

	public void Play(string file)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
			{
				_logger.LogWarning("Focus item sound file not found: {File}", file);
				return;
			}

			PlaySound(file, IntPtr.Zero, SndAsync | SndFilename);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Could not play the Focus item sound {File}", file);
		}
	}
}
