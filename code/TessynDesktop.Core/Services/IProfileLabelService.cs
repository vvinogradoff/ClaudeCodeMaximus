using System.Threading.Tasks;

namespace TessynDesktop.Services;

/// <summary>
/// The display label of the Default profile (the base, non-isolated Claude account): "[Sys] &lt;account e-mail&gt;" once the
/// e-mail is known, "[Sys] Default" until then — the same label the desktop's profile combo shows and stamps on USER entries.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IProfileLabelService
{
	/// <summary>
	/// The Default label, resolving the account e-mail through the CLI when it is not cached. Never throws and never waits
	/// longer than <c>Constants.Remote.Profiles.LabelResolveTimeout</c>; falls back to "[Sys] Default".
	/// </summary>
	Task<string> GetDefaultLabelAsync();

	/// <summary>
	/// The Default label without waiting: the cached one, or the fallback while a background resolution is started.
	/// </summary>
	string GetDefaultLabelCached();
}
