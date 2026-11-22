namespace TessynDesktop.Models.Remote;

/// <summary>One row of the GPT model tier table (FR.33.14): a GPT model id that contains <paramref name="Match"/> (any case) has <paramref name="Tier"/>; rows are tried top first.</summary>
/// <param name="Match">The text to look for in the model id.</param>
/// <param name="Tier">0 (local) to 4 (Fable); Opus is 3, Sonnet 2, Haiku 1.</param>
/// <remarks>Created by Claude</remarks>
public sealed record CodexModelTierDto(string Match, int Tier);
