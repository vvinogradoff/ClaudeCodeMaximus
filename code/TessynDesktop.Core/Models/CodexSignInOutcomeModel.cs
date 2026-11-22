namespace TessynDesktop.Models;

/// <summary>How a ChatGPT sign-in ended (FR.33.5): the profile with its e-mail and plan, or Codex's error text (nothing is added then).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record CodexSignInOutcomeModel(bool Success, string ProfileId, string? Email, string? PlanType, string? Error);
