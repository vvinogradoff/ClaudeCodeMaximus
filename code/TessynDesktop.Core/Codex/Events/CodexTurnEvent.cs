namespace TessynDesktop.Codex.Events;

/// <summary>
/// One neutral event of a Codex turn (FR.33.11), produced by <see cref="TessynDesktop.Codex.CodexTurnMapper"/> from the app-server's
/// notifications. Phase 2b adapts these to the engine seam's sink, so the transcript, <c>turn.*</c> progress, Focus, Telegram and the Web
/// client need no Codex-specific code.
/// </summary>
/// <remarks>Created by Claude</remarks>
public abstract record CodexTurnEvent;
