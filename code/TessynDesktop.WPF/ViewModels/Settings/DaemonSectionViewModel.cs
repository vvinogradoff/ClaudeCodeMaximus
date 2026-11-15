using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.ViewModels.Settings;

/// <summary>
/// A versioned daemon section (<c>settings.get</c> / <c>settings.update</c>): the editor fields hold the user's edits, <c>Snapshot</c> what the daemon last
/// answered. Saving sends only the members that differ from the snapshot, with the version the snapshot had. A stale version
/// (<see cref="Constants.Settings.StaleVersionMessage"/>) reloads the section and puts the user's edits back on top, so nothing typed is lost and nothing is overwritten blindly.
/// </summary>
/// <remarks>Created by Claude</remarks>
public abstract partial class DaemonSectionViewModel<TValues, TPatch> : SettingsSectionViewModel, ISettingsEditor
	where TValues : class
	where TPatch : class
{
	private readonly ILogger _logger;
	private long _version;

	protected DaemonSectionViewModel(ITessynClient client, ILogger logger)
	{
		Client = client;
		_logger = logger;
	}

	protected ITessynClient Client { get; }

	/// <summary>The values the daemon last answered; null before the first load.</summary>
	protected TValues? Snapshot { get; private set; }

	/// <summary>The daemon's section name (<c>general</c>, <c>notifications</c>, <c>telegram</c>).</summary>
	public abstract string SectionName { get; }

	public bool IsLoaded => Snapshot != null;

	public bool HasChanges
	{
		get
		{
			if (Snapshot == null)
				return false;

			try
			{
				return BuildPatch(Snapshot) != null;
			}
			catch (SettingsInputException)
			{
				return true;
			}
		}
	}

	/// <summary>Puts the daemon's values into the editor fields.</summary>
	protected abstract void ReadFrom(TValues values);

	/// <summary>The members that differ from <paramref name="snapshot"/>, or null when none do. Throws <see cref="SettingsInputException"/> for an entry that cannot be sent.</summary>
	protected abstract TPatch? BuildPatch(TValues snapshot);

	/// <summary>Puts the edits of an earlier patch back into the editor fields after a reload.</summary>
	protected abstract void ApplyEdits(TPatch edits);

	/// <summary>Called after a load, so a section can read what only it needs (the sound list, a status).</summary>
	protected virtual Task AfterLoadAsync() => Task.CompletedTask;

	/// <summary>Reads the section; a failure is shown as the section's error and leaves the editor as it was.</summary>
	[RelayCommand]
	public async Task ReloadAsync()
	{
		ClearMessages();
		await LoadCoreAsync();
	}

	private async Task<bool> LoadCoreAsync()
	{
		try
		{
			var result = await Client.SettingsGetAsync<TValues>(SectionName);
			_version = result.Version;
			Snapshot = result.Values;
			ReadFrom(result.Values);
			OnPropertyChanged(nameof(IsLoaded));
			await AfterLoadAsync();
			return true;
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Reading the settings section {Section} failed", SectionName);
			ErrorText = ex.Message;
			return false;
		}
	}

	public async Task<bool> SaveAsync()
	{
		if (Snapshot == null)
			return true;

		TPatch? patch;
		try
		{
			patch = BuildPatch(Snapshot);
		}
		catch (SettingsInputException ex)
		{
			StatusText = null;
			ErrorText = string.Format(Constants.Settings.SaveFailedFormat, ex.Message);
			return false;
		}

		ClearMessages();
		if (patch == null)
			return true;

		try
		{
			var result = await Client.SettingsUpdateAsync(SectionName, patch, _version);
			_version = result.Version;
			await LoadCoreAsync();
			if (!HasError)
				StatusText = Constants.Settings.Saved;

			return !HasError;
		}
		catch (TessynRpcException ex) when (IsStale(ex))
		{
			// Someone else saved this section meanwhile: take the new values, then lay the user's edits over them.
			if (await LoadCoreAsync())
			{
				ApplyEdits(patch);
				ErrorText = null;
				StatusText = string.Format(Constants.Settings.ConflictFormat, Title);
			}

			return false;
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Saving the settings section {Section} failed", SectionName);
			ErrorText = string.Format(Constants.Settings.SaveFailedFormat, ex.Message);
			return false;
		}
	}

	/// <summary>A <c>settings.changed</c> for this section arrived: reload it when nothing was edited, else only say so.</summary>
	public async Task OnChangedElsewhereAsync(long version)
	{
		if (Snapshot == null || version <= _version)
			return;

		if (HasChanges)
		{
			StatusText = string.Format(Constants.Settings.ChangedElsewhereFormat, Title);
			return;
		}

		await LoadCoreAsync();
	}

	/// <summary>True for the daemon's "Settings changed" answer to an update with an old version.</summary>
	public static bool IsStale(TessynRpcException ex) =>
		ex.Code == RemoteProtocol.ErrorCodes.DaemonError && ex.Message == Constants.Settings.StaleVersionMessage;

	/// <summary>Called by the window once, to read the section.</summary>
	public async Task InitializeAsync(string capability)
	{
		if (Client.ServerInfo?.Capabilities.Contains(capability) != true)
		{
			MakeUnavailable(capability);
			return;
		}

		await LoadCoreAsync();
	}
}
