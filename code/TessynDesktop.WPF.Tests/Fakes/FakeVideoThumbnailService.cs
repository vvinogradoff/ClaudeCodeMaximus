using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>Stands in for the shell: writes a small file as the "frame" (or fails when <see cref="Succeeds"/> is false) and records the video it was given.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FakeVideoThumbnailService : IVideoThumbnailService
{
	public bool Succeeds { get; set; } = true;

	public List<string> Videos { get; } = [];

	/// <summary>Whether the video file existed when the shell was asked (it must: the shell reads it from the preview cache).</summary>
	public List<bool> VideoExisted { get; } = [];

	public Task<bool> TryCreateAsync(string videoPath, string pngPath, int maxPixels, CancellationToken cancellationToken)
	{
		Videos.Add(videoPath);
		VideoExisted.Add(File.Exists(videoPath));
		if (Succeeds)
			File.WriteAllBytes(pngPath, [0x89, 0x50, 0x4E, 0x47]);

		return Task.FromResult(Succeeds);
	}
}
