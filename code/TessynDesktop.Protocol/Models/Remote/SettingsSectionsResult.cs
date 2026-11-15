using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>Result of <c>settings.sections</c>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record SettingsSectionsResult(IReadOnlyList<SettingsSectionInfoDto> Sections);
