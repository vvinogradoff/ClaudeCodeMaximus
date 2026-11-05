using System;

namespace TessynDesktop.Models;

/// <summary>Sort key for ranking a session in the tree search results (FR.1.12).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record SearchRelevanceKeyModel(int StrictMatchCount, int LooseMatchCount, DateTimeOffset? LastPromptTimestamp);
