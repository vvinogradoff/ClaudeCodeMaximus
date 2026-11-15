namespace TessynDesktop.WPF.Transcript;

/// <summary>One tool call of the live row: started, then done (with or without an error).</summary>
/// <param name="Id">The tool use id that pairs the started and the result notification.</param>
/// <param name="Name">The tool's name (for instance Bash, Read).</param>
/// <param name="Summary">One line about the call, truncated by the daemon.</param>
/// <param name="Done">The result came back.</param>
/// <param name="IsError">The result reported an error.</param>
/// <remarks>Created by Claude</remarks>
public sealed record TranscriptLiveTool(string Id, string Name, string? Summary, bool Done, bool IsError);
