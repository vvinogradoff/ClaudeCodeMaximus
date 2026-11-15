namespace TessynDesktop.WPF.Services;

/// <summary>The media proxy refused a request (policy, redirect, content type, size) or could not fetch it.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class MediaRefusedException : Exception
{
	public MediaRefusedException(string message) : base(message)
	{
	}
}
