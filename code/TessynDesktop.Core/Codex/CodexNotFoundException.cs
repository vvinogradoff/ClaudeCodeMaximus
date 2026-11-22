using System;

namespace TessynDesktop.Codex;

/// <summary>No usable <c>codex.exe</c> was found (FR.33.17); the message says why (for example the install hint).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexNotFoundException : Exception
{
	public CodexNotFoundException(string message) : base(message)
	{
	}
}
