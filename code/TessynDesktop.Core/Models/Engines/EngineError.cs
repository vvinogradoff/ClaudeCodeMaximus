using System;

namespace TessynDesktop.Models.Engines;

/// <summary>
/// An error outside the turn's result: the process failed to start, wrote an error to stderr or exited abnormally. An error that ends the turn
/// comes with <see cref="EngineTurnEnd"/> instead. The engine classifies it, so consumers never match engine-specific texts.
/// </summary>
/// <param name="Text">The error text (never blank).</param>
/// <param name="IsAuthError">The credentials of the profile are invalid (FR.12.16).</param>
/// <param name="IsLimitError">A usage or rate limit was hit (FR.1.29).</param>
/// <param name="Timestamp">When the engine reported it.</param>
/// <remarks>Created by Claude</remarks>
public sealed record EngineError(string Text, bool IsAuthError, bool IsLimitError, DateTimeOffset Timestamp);
