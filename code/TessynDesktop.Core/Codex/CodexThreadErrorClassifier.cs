using System;

namespace TessynDesktop.Codex;

/// <summary>
/// Tells the thread failures of FR.33.7 apart. Codex 0.171 answers them all with <c>-32600</c> and no <c>data</c>, so the message text is
/// the only signal (spike item 9).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexThreadErrorClassifier
{
	public CodexThreadErrorKind Classify(CodexRpcException error)
	{
		if (error.Code != Constants.Codex.Rpc.InvalidRequest)
			return CodexThreadErrorKind.Unknown;

		var message = error.Message;
		if (Contains(message, Constants.Codex.ThreadErrorMessages.ActiveWriter))
			return CodexThreadErrorKind.ActiveWriter;
		if (Contains(message, Constants.Codex.ThreadErrorMessages.NoRollout) || Contains(message, Constants.Codex.ThreadErrorMessages.InvalidSessionId))
			return CodexThreadErrorKind.ThreadGone;
		if (Contains(message, Constants.Codex.ThreadErrorMessages.ThreadNotFound))
			return CodexThreadErrorKind.NotLoaded;

		return CodexThreadErrorKind.Unknown;
	}

	private static bool Contains(string message, string fragment) => message.Contains(fragment, StringComparison.OrdinalIgnoreCase);
}
