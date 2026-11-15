using TessynDesktop.Models.Remote;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Client;

/// <summary>Test module: <c>attachment.import</c> and <c>attachment.list</c> that answer only after a delay (a big file on a slow disk).</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class SlowAttachmentModule : IRemoteRpcModule
{
	private readonly TimeSpan _delay;

	public SlowAttachmentModule(TimeSpan delay)
	{
		_delay = delay;
	}

	public void Register(TdcpMethodRegistry registry)
	{
		registry.Add(RemoteProtocol.LoopbackMethods.AttachmentImport, async (p, ct) =>
		{
			await Task.Delay(_delay, ct);
			return new AttachmentDto("att.slow", @"C:\slow\big.bin", "big.bin", 1, "application/octet-stream", DateTimeOffset.UnixEpoch);
		}, remoteCallable: false);
		registry.Add(RemoteProtocol.Methods.AttachmentList, async (p, ct) =>
		{
			await Task.Delay(_delay, ct);
			return new AttachmentListResult("n", []);
		}, remoteCallable: true);
	}
}
