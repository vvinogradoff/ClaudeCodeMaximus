using System;
using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>Result of <c>import.titles</c>: the titles known now; the ones still missing are being generated.</summary>
/// <param name="Titles">The titles that exist now.</param>
/// <param name="Pending">How many of the requested sessions are still waiting for a title; ask again until it is 0.</param>
/// <remarks>Created by Claude</remarks>
public sealed record ImportTitlesResult(IReadOnlyList<ImportTitleDto> Titles, int Pending);
