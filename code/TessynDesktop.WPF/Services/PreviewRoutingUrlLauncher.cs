using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// <see cref="IUrlLauncher"/> that offers every http(s) address to the <see cref="IPreviewRouter"/> first (FR.29.7): transcript links, tag chips, Focus card
/// links and links inside previews then open in the matching group's window. Anything the router does not take goes to the <c>fallback</c> (the system browser).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class PreviewRoutingUrlLauncher : IUrlLauncher
{
	private readonly IPreviewRouter _router;
	private readonly IUrlLauncher _fallback;
	private readonly UrlPolicy _policy;

	public PreviewRoutingUrlLauncher(IPreviewRouter router, IUrlLauncher fallback, UrlPolicy policy)
	{
		_router = router;
		_fallback = fallback;
		_policy = policy;
	}

	public bool TryOpen(string? url)
	{
		if (!_policy.TryParse(url, out var uri))
			return _fallback.TryOpen(url);   // refuses and logs as before

		var item = new PreviewItemModel { Kind = PreviewItemKind.Url, Url = uri.AbsoluteUri, SourceNodeId = _router.CurrentNodeId };
		return _router.TryOpenUrl(item) || _fallback.TryOpen(url);
	}
}
