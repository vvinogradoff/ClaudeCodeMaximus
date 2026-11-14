namespace TessynDesktop.Client;

/// <summary>The daemon answered a JSON-RPC call with an error object.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TessynRpcException : Exception
{
	public TessynRpcException(int code, string message) : base(message)
	{
		Code = code;
	}

	/// <summary>The JSON-RPC error code (see <c>RemoteProtocol.ErrorCodes</c>).</summary>
	public int Code { get; }
}
