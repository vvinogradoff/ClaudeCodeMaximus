using System.Threading.Channels;

namespace Tessyn.Web.Link;

/// <summary>A browser's live subscription: frames to send first (status, resync, replay) then a live channel.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class EventSubscription
{
	private readonly Channel<BrowserFrame> _live = Channel.CreateBounded<BrowserFrame>(
		new BoundedChannelOptions(Constants.Limits.SubscriberQueue) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

	public EventSubscription(long id, IReadOnlyList<BrowserFrame> initial)
	{
		Id = id;
		Initial = initial;
	}

	public long Id { get; }
	public IReadOnlyList<BrowserFrame> Initial { get; }
	public ChannelReader<BrowserFrame> Live => _live.Reader;

	/// <summary>Returns false when the subscriber is too slow; it is completed so the client reconnects and resumes.</summary>
	public bool TryPush(BrowserFrame frame)
	{
		if (_live.Writer.TryWrite(frame))
			return true;

		_live.Writer.TryComplete();
		return false;
	}

	public void Complete()
	{
		_live.Writer.TryComplete();
	}
}
