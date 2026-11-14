using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>Result of <c>tags.search</c> (contract 3.8).</summary>
/// <param name="EmptyReason">Why there are no chips; omitted when there are.</param>
/// <param name="Chips">Selected tags first, then the other matches, each part in FR.24.11 order.</param>
/// <param name="SessionsEmptyReason">Why there are no session groups; omitted when there are.</param>
/// <param name="Groups">Sessions carrying any selected tag, grouped by directory like the Recent tab.</param>
/// <remarks>Created by Claude</remarks>
public sealed record TagSearchResult(
	TagEmptyReason? EmptyReason,
	IReadOnlyList<TagChipDto> Chips,
	TagSessionsEmptyReason? SessionsEmptyReason,
	IReadOnlyList<SessionGroupDto> Groups);
