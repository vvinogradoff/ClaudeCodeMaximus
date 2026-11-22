using System.Collections.Generic;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Contract;

/// <summary>A scripted <see cref="ISessionEngineAvailability"/>: the engines a test says were found.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class FixedEngineAvailability : ISessionEngineAvailability
{
	public List<string> Available { get; set; } = [];

	public IReadOnlyList<string> GetAvailableEngines() => [.. Available];
}
