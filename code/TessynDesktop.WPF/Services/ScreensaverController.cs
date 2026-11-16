using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Services;

/// <summary>Lists the images of the screensaver folder, in name order; replaced in tests.</summary>
/// <remarks>Created by Claude</remarks>
public interface IScreensaverImages
{
	IReadOnlyList<string> List(string directory);
}

/// <summary>Lists the image files of a folder (png, jpg, jpeg, bmp, gif, webp), sorted by path; a missing or empty folder gives none.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FolderScreensaverImages : IScreensaverImages
{
	public IReadOnlyList<string> List(string directory)
	{
		if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
			return [];

		try
		{
			return Directory.EnumerateFiles(directory)
				.Where(f => Constants.Screensaver.ImageExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
				.Order(StringComparer.OrdinalIgnoreCase)
				.ToList();
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return [];
		}
	}
}

/// <summary>
/// The screensaver's rules, free of any window: it starts after <see cref="ScreensaverSettings.TimeoutSeconds"/> without input (0 = off), shows one image at a time
/// and moves to the next every <see cref="ScreensaverSettings.SlideshowIntervalSeconds"/>, and any input ends it. The view feeds it the clock and the input;
/// it reads the settings on every tick, so a change in Settings applies at once. Holds the path of the image on show, never the image.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ScreensaverController
{
	private readonly IClientSettingsStore _store;
	private readonly IScreensaverImages _images;
	private IReadOnlyList<string> _list = [];
	private DateTime _lastActivity;
	private DateTime _lastChange;
	private int _index;

	public ScreensaverController(IClientSettingsStore store, IScreensaverImages images, DateTime now)
	{
		_store = store;
		_images = images;
		_lastActivity = now;
		_lastChange = now;
	}

	/// <summary>The screensaver covers the window.</summary>
	public bool IsActive { get; private set; }

	/// <summary>The image on show; null while inactive or when the folder has none (the cover is then plain black).</summary>
	public string? CurrentImage => IsActive && _list.Count > 0 ? _list[_index % _list.Count] : null;

	/// <summary>The cover appeared, went, or moved to another image.</summary>
	public event EventHandler? Changed;

	/// <summary>The user did something. While the cover is up this ends it (the input is then consumed by the caller).</summary>
	/// <returns>True when this input ended the screensaver.</returns>
	public bool NoteInput(DateTime now)
	{
		_lastActivity = now;
		if (!IsActive)
			return false;

		IsActive = false;
		_list = [];
		Changed?.Invoke(this, EventArgs.Empty);
		return true;
	}

	/// <summary>Called about once a second.</summary>
	public void Tick(DateTime now)
	{
		var settings = _store.Get<ScreensaverSettings>();
		if (settings.TimeoutSeconds <= 0)
		{
			if (IsActive)
				NoteInput(now);

			return;
		}

		if (!IsActive)
		{
			if ((now - _lastActivity).TotalSeconds < settings.TimeoutSeconds)
				return;

			_list = _images.List(settings.Directory);
			_index = 0;
			_lastChange = now;
			IsActive = true;
			Changed?.Invoke(this, EventArgs.Empty);
			return;
		}

		if (_list.Count > 1 && (now - _lastChange).TotalSeconds >= Math.Max(settings.SlideshowIntervalSeconds, Constants.Settings.MinScreensaverSeconds))
		{
			_index = (_index + 1) % _list.Count;
			_lastChange = now;
			Changed?.Invoke(this, EventArgs.Empty);
		}
	}
}
