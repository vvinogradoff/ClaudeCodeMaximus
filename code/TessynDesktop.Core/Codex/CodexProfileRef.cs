namespace TessynDesktop.Codex;

/// <summary>
/// A Codex profile as the host pool needs it (FR.33.5): its id and its <c>CODEX_HOME</c> folder. <see cref="CodexHome"/> null is Codex Default
/// (<c>%USERPROFILE%\.codex</c>, launched without <c>CODEX_HOME</c>).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed record CodexProfileRef(string ProfileId, string? CodexHome);
