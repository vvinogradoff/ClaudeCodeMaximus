namespace TessynDesktop.Client;

/// <summary>The daemon rejected the token (HTTP 401) or the request origin (HTTP 403).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TessynAuthException : Exception
{
	public TessynAuthException(int httpStatus, string message) : base(message)
	{
		HttpStatus = httpStatus;
	}

	/// <summary>401 or 403.</summary>
	public int HttpStatus { get; }
}
