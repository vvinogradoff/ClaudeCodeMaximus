using Microsoft.Extensions.Options;
using Tessyn.Web.Options;

namespace Tessyn.Web.Link;

/// <summary>Bounds the uploads in flight: each one holds a daemon dispatch worker while the daemon downloads it, so a few stalled ones must not starve every other relay call.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class UploadLimiter
{
	private readonly SemaphoreSlim _slots;

	public UploadLimiter(IOptions<TessynWebOptions> options)
	{
		_slots = new SemaphoreSlim(Math.Max(1, options.Value.Attachments.MaxConcurrentUploads));
	}

	/// <summary>Takes a slot without waiting; false when all are taken.</summary>
	public bool TryEnter() => _slots.Wait(0);

	public void Exit() => _slots.Release();
}
