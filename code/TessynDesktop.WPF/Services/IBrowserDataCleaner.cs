namespace TessynDesktop.WPF.Services;

/// <summary>Deletes the embedded browser's stored data: cookies, storage, cache, history and passwords (FR.27.8).</summary>
/// <remarks>Created by Claude</remarks>
public interface IBrowserDataCleaner
{
	/// <summary>Clears everything; works with no browser window open. False (and logged) on failure. The caller asks for confirmation first.</summary>
	Task<bool> ClearAsync();
}
