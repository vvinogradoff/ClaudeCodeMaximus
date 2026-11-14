namespace TessynDesktop.WPF.Transcript;

/// <summary>The page painted a loaded session.</summary>
/// <param name="Key">The key passed to <see cref="ITranscriptSurface.Load"/> (the session's node id).</param>
/// <param name="JsMilliseconds">Time the page's script spent building the first window.</param>
/// <remarks>Created by Claude</remarks>
public sealed record TranscriptPaintedEventArgs(string Key, double JsMilliseconds);
