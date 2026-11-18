using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Services;

/// <summary>The group an item belongs to and the pattern that matched it.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record PreviewGroupMatch(PreviewGroupModel Group, PreviewPattern Pattern);
