using System;
using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>Result of <c>import.run</c>: every requested session is in exactly one of the two lists.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record ImportRunResult(IReadOnlyList<ImportedSessionDto> Imported, IReadOnlyList<ImportFailureDto> Failed);
