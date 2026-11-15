namespace Tessyn.Web.Link;

/// <summary>The only JSON-RPC methods browsers may call (contract section 3).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class RemoteMethodAllowlist
{
	private static readonly string[] AllowedMethods =
	[
		Constants.Remote.Methods.ClientHello,
		Constants.Remote.Methods.TreeSnapshot,
		Constants.Remote.Methods.TreeWorkers,
		Constants.Remote.Methods.TreeSearch,
		Constants.Remote.Methods.TreeCreateSession,
		Constants.Remote.Methods.RecentList,
		Constants.Remote.Methods.RecentHide,
		Constants.Remote.Methods.TagsSearch,
		Constants.Remote.Methods.SessionGet,
		Constants.Remote.Methods.SessionEntries,
		Constants.Remote.Methods.SessionEntry,
		Constants.Remote.Methods.SessionSend,
		Constants.Remote.Methods.SessionStop,
		Constants.Remote.Methods.SessionCompact,
		Constants.Remote.Methods.SessionSetFavorite,
		Constants.Remote.Methods.SessionSetStreaming,
		Constants.Remote.Methods.AttentionList,
		Constants.Remote.Methods.AttentionReply,
		Constants.Remote.Methods.AttentionDismiss,
		Constants.Remote.Methods.CatalogGet,
		Constants.Remote.Methods.UsageGet,
		Constants.Remote.Methods.FileRead,
		Constants.Remote.Methods.FilePreview,
	];

	private static readonly string[] LongMethods =
	[
		Constants.Remote.Methods.SessionEntries,
		Constants.Remote.Methods.TreeSnapshot,
		Constants.Remote.Methods.TagsSearch,
		Constants.Remote.Methods.TreeSearch,
		Constants.Remote.Methods.FileRead,
		Constants.Remote.Methods.FilePreview,
	];

	private readonly HashSet<string> _allowed = new(AllowedMethods, StringComparer.Ordinal);
	private readonly HashSet<string> _long = new(LongMethods, StringComparer.Ordinal);

	public IReadOnlyCollection<string> Methods => AllowedMethods;

	public bool IsAllowed(string method)
	{
		return _allowed.Contains(method);
	}

	/// <summary>Content-heavy methods get the longer server timeout.</summary>
	public bool IsLong(string method)
	{
		return _long.Contains(method);
	}
}
