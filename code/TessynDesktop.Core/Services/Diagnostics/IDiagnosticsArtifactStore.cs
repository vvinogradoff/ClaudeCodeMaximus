using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>Owns the diag folder: names new artifacts and prunes old ones.</summary>
/// <remarks>Created by Claude</remarks>
public interface IDiagnosticsArtifactStore
{
	string Folder { get; }

	/// <summary>Creates the folder if needed and returns a fresh timestamped path for <paramref name="kind"/>.</summary>
	string CreatePath(DiagnosticArtifactKind kind);

	/// <summary>Deletes the oldest files of <paramref name="kind"/> beyond the configured retention count.</summary>
	void EnforceRetention(DiagnosticArtifactKind kind);
}
