using System.Threading.Channels;

namespace Tessyn.Web.Link;

/// <summary>One connected daemon stream: a queue of JSON-RPC request frames to write down the SSE response.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class DaemonLink
{
	private readonly Channel<string> _frames = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });

	public DaemonLink(string daemonName, DateTimeOffset since)
	{
		DaemonName = daemonName;
		Since = since;
	}

	public string DaemonName { get; }
	public DateTimeOffset Since { get; }
	public ChannelReader<string> Frames => _frames.Reader;

	public bool TryEnqueue(string frame)
	{
		return _frames.Writer.TryWrite(frame);
	}

	public void Complete()
	{
		_frames.Writer.TryComplete();
	}
}
