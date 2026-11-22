namespace TessynDesktop;

/// <remarks>Created by Claude</remarks>
public static partial class Constants
{
	/// <summary>The session engines (FR.33.1): the ids stored in <c>SessionNodeModel.EngineId</c> and reported by each engine.</summary>
	public static class SessionEngines
	{
		/// <summary>The Claude Code CLI (FR.5-FR.19). A null engine id means this engine as well.</summary>
		public const string Claude = "claude";

		/// <summary>The Codex CLI's app-server, GPT models on the ChatGPT plan (FR.33).</summary>
		public const string Codex = "codex";

		/// <summary>How each engine is named in the transcript (FR.33.4).</summary>
		public const string ClaudeDisplayName = "Claude";
		public const string CodexDisplayName = "Codex (GPT)";

		/// <summary>FR.33.4: the SYSTEM entry of the first turn on a new engine. Arg 0 = the old engine's name, arg 1 = the new one's.</summary>
		public const string EngineChangedFormat = "Engine changed: {0} -> {1}. The conversation continues from the transcript.";
	}
}
