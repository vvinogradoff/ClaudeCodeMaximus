using TessynDesktop.Models;
using TessynDesktop.Services;

namespace Tessyn.Web.E2E.Tests.Harness;

/// <summary>Every Claude session is resumable; nothing is adopted across profiles.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FakeSessionStatusService : IClaudeSessionStatusService
{
	public bool IsSessionResumable(string workingDirectory, string claudeSessionId, IReadOnlyList<string>? projectsDirs = null) => true;

	public IReadOnlyList<string> GetAllProjectsDirs() => [];

	public IReadOnlyList<bool> AreSessionsResumable(IReadOnlyList<SessionResumabilityRequest> requests, IReadOnlyList<string>? projectsDirs = null) =>
		[.. requests.Select(_ => true)];

	public bool TryAdoptSessionFromOtherProfile(string workingDirectory, string claudeSessionId, string? activeProfileConfigDir) => false;
}
