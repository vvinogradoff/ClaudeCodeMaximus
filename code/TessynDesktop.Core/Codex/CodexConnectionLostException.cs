using System;

namespace TessynDesktop.Codex;

/// <summary>The app-server's output ended (the process exited or crashed) while a call was waiting, or a call was made after it ended.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexConnectionLostException : Exception
{
	public CodexConnectionLostException(string message, Exception? inner = null) : base(message, inner) { }
}
