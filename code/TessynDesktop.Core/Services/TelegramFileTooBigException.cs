using System;

namespace TessynDesktop.Services;

/// <summary>Thrown when the Bot API refuses <c>getFile</c> because the file exceeds the 20 MB download limit (FR.21.14).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TelegramFileTooBigException : InvalidOperationException
{
	public TelegramFileTooBigException(string message) : base(message) { }
}
