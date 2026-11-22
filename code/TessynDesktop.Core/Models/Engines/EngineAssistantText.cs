using System;

namespace TessynDesktop.Models.Engines;

/// <summary>One assistant message of a turn, as an engine reports it.</summary>
/// <param name="Text">The message text (never blank).</param>
/// <param name="Timestamp">When the engine produced it.</param>
/// <param name="IsLimitError">The engine marked the message as a usage or rate-limit error (FR.1.29), not as ordinary prose.</param>
/// <remarks>Created by Claude</remarks>
public sealed record EngineAssistantText(string Text, DateTimeOffset Timestamp, bool IsLimitError);
