using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.ViewModels.Settings;

/// <summary>
/// The Codex part of the General tab (FR.33.17): the Codex path with Browse, the read-only status line and version note, the catalog profile and the
/// editable GPT model tier table (FR.33.14). Like the rest of the tab it is saved with the window's Save button; <b>Reset to defaults</b> is an edit too
/// (sent as <c>resetCodexModelTiers</c>), so the close guard (FR.4.7) covers it.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class GeneralSectionViewModel
{
	private readonly Services.IFilePicker? _filePicker;
	private readonly ILogger _codexLogger;

	/// <summary>The daemon has a Codex engine (it sent <c>codexStatus</c>); the section is hidden otherwise.</summary>
	[ObservableProperty]
	private bool _hasCodexEngine;

	/// <summary>FR.33.17: an absolute path to a native <c>codex.exe</c>; empty finds it.</summary>
	[ObservableProperty]
	private string _codexPath = string.Empty;

	/// <summary><c>Codex 0.171.0 found at ...</c>, or not found with the winget hint (the daemon's text).</summary>
	[ObservableProperty]
	private string _codexStatusLine = string.Empty;

	[ObservableProperty]
	private bool _isCodexFound;

	/// <summary>FR.33.6: the version note (a version other than the tested one) plus the tested version; empty when there is nothing to say.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasCodexNote))]
	private string _codexNote = string.Empty;

	/// <summary>FR.33.2: the ChatGPT profile the GPT model list comes from; empty = automatic.</summary>
	[ObservableProperty]
	private string _codexCatalogProfileId = string.Empty;

	/// <summary>Reset to defaults was pressed and not saved yet.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(TierTableNote))]
	[NotifyPropertyChangedFor(nameof(AreTiersEditable))]
	[NotifyCanExecuteChangedFor(nameof(ResetTiersCommand))]
	private bool _isTierResetPending;

	/// <summary>The rows are read-only while a reset waits for Save.</summary>
	public bool AreTiersEditable => !IsTierResetPending;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(TierTableNote))]
	[NotifyCanExecuteChangedFor(nameof(ResetTiersCommand))]
	private bool _tiersAreDefaults = true;

	/// <summary>The version this build was tested with (<c>Tested with Codex 0.171.x.</c>).</summary>
	[ObservableProperty]
	private string _codexTestedText = string.Empty;

	public bool HasCodexNote => !string.IsNullOrEmpty(CodexNote);

	public ObservableCollection<SettingOption<string>> CatalogProfiles { get; } = [];

	public ObservableCollection<CodexTierRowViewModel> Tiers { get; } = [];

	public IReadOnlyList<SettingOption<int>> TierChoices { get; } =
		Enumerable.Range(Constants.Codex.MinTier, Constants.Codex.MaxTier - Constants.Codex.MinTier + 1).Select(t => new SettingOption<int>(t, Constants.Codex.TierLabels[t])).ToList();

	public string TierTableNote => IsTierResetPending ? Constants.Codex.TiersResetPending : TiersAreDefaults ? Constants.Codex.TiersAreDefaults : string.Empty;

	private void ReadCodexFrom(GeneralSettingsDto values)
	{
		HasCodexEngine = values.CodexStatus != null;
		CodexPath = values.CodexPath;
		CodexStatusLine = values.CodexStatus?.StatusLine ?? string.Empty;
		IsCodexFound = values.CodexStatus?.Found == true;
		CodexNote = values.CodexStatus?.Note ?? string.Empty;
		CodexTestedText = values.CodexStatus == null ? string.Empty : string.Format(Constants.Codex.TestedVersionFormat, values.CodexStatus.TestedVersion);
		CodexCatalogProfileId = values.CodexCatalogProfileId ?? string.Empty;
		TiersAreDefaults = values.CodexModelTiersAreDefaults;
		IsTierResetPending = false;
		Tiers.Clear();
		foreach (var row in values.CodexModelTiers ?? [])
			Tiers.Add(new CodexTierRowViewModel(row));
		EnsureCatalogProfileListed(CodexCatalogProfileId);
	}

	/// <summary>The Codex members of the patch: each only when it differs from what the daemon answered.</summary>
	private (string? Path, string? CatalogProfile, IReadOnlyList<CodexModelTierDto>? Tiers, bool? Reset) BuildCodexPatch(GeneralSettingsDto snapshot)
	{
		if (snapshot.CodexStatus == null)
			return (null, null, null, null);

		var path = CodexPath.Trim();
		var catalog = CodexCatalogProfileId.Trim();
		if (IsTierResetPending)
			return (path != snapshot.CodexPath ? path : null, catalog != (snapshot.CodexCatalogProfileId ?? string.Empty) ? catalog : null, null, true);

		var rows = ValidatedTiers();
		var tiersChanged = !rows.SequenceEqual(snapshot.CodexModelTiers ?? []);
		return (path != snapshot.CodexPath ? path : null, catalog != (snapshot.CodexCatalogProfileId ?? string.Empty) ? catalog : null, tiersChanged ? rows : null, null);
	}

	private void ApplyCodexEdits(GeneralSettingsPatch edits)
	{
		if (edits.CodexPath != null)
			CodexPath = edits.CodexPath;
		if (edits.CodexCatalogProfileId != null)
		{
			CodexCatalogProfileId = edits.CodexCatalogProfileId;
			EnsureCatalogProfileListed(CodexCatalogProfileId);
		}

		if (edits.ResetCodexModelTiers == true)
			IsTierResetPending = true;
		else if (edits.CodexModelTiers != null)
		{
			Tiers.Clear();
			foreach (var row in edits.CodexModelTiers)
				Tiers.Add(new CodexTierRowViewModel(row));
		}
	}

	/// <summary>The table as it would be sent; throws <see cref="SettingsInputException"/> for a row the daemon would refuse.</summary>
	private List<CodexModelTierDto> ValidatedTiers()
	{
		if (Tiers.Count > Constants.Codex.MaxTierRows)
			throw new SettingsInputException(string.Format(Constants.Codex.TooManyTierRowsFormat, Constants.Codex.MaxTierRows));

		var rows = Tiers.Select(t => t.ToDto()).ToList();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var row in rows)
		{
			if (row.Match.Length == 0)
				throw new SettingsInputException(Constants.Codex.TierMatchRequired);
			if (row.Match.Length > Constants.Codex.MaxTierMatchChars)
				throw new SettingsInputException(string.Format(Constants.Codex.TierMatchTooLongFormat, Constants.Codex.MaxTierMatchChars));
			if (!seen.Add(row.Match))
				throw new SettingsInputException(string.Format(Constants.Codex.TierMatchDuplicateFormat, row.Match));
		}

		return rows;
	}

	/// <summary>Reads the signed-in ChatGPT profiles for the catalog profile drop-down (FR.33.2); a failure leaves only Automatic and the stored id.</summary>
	private async Task LoadCatalogProfilesAsync()
	{
		if (!HasCodexEngine)
			return;

		var options = new List<SettingOption<string>> { new(string.Empty, Constants.Codex.AutomaticCatalogProfile) };
		if (Client.ServerInfo?.Capabilities.Contains(RemoteProtocol.ProfilesAdmin.Capability) == true)
		{
			try
			{
				var list = await Client.ProfileListAsync();
				options.AddRange((list.CodexProfiles ?? []).Where(p => p.SignedIn).Select(p => new SettingOption<string>(p.Id, p.DisplayName)));
			}
			catch (Exception ex)
			{
				_codexLogger.LogWarning(ex, "Reading the ChatGPT profiles for the catalog profile failed");
			}
		}

		var selected = CodexCatalogProfileId;
		CatalogProfiles.Clear();
		foreach (var option in options)
			CatalogProfiles.Add(option);
		EnsureCatalogProfileListed(selected);
		CodexCatalogProfileId = selected;   // the drop-down lost its selection while the list was refilled
	}

	/// <summary>A stored catalog profile that is not signed in any more stays visible (by its id), so loading the tab never changes it.</summary>
	private void EnsureCatalogProfileListed(string id)
	{
		if (CatalogProfiles.Count == 0)
			CatalogProfiles.Add(new SettingOption<string>(string.Empty, Constants.Codex.AutomaticCatalogProfile));
		if (CatalogProfiles.All(o => o.Value != id))
			CatalogProfiles.Add(new SettingOption<string>(id, id));
	}

	private bool CanBrowseCodexPath() => _filePicker != null;

	[RelayCommand(CanExecute = nameof(CanBrowseCodexPath))]
	private async Task BrowseCodexPathAsync()
	{
		var picked = await _filePicker!.PickAsync(Constants.Codex.BrowseCodexTitle, Constants.Codex.BrowseCodexFilter, CodexPath);
		if (!string.IsNullOrEmpty(picked))
			CodexPath = picked;
	}

	[RelayCommand]
	private void AddTier()
	{
		if (!IsTierResetPending)
			Tiers.Add(new CodexTierRowViewModel(Constants.Codex.NewTierMatch, Constants.Codex.NewTierLevel));
	}

	[RelayCommand]
	private void RemoveTier(CodexTierRowViewModel? row)
	{
		if (row != null)
			Tiers.Remove(row);
	}

	[RelayCommand]
	private void MoveTierUp(CodexTierRowViewModel? row) => MoveTier(row, -1);

	[RelayCommand]
	private void MoveTierDown(CodexTierRowViewModel? row) => MoveTier(row, 1);

	private void MoveTier(CodexTierRowViewModel? row, int by)
	{
		var index = row == null ? -1 : Tiers.IndexOf(row);
		var target = index + by;
		if (index < 0 || target < 0 || target >= Tiers.Count)
			return;

		Tiers.Move(index, target);
	}

	private bool CanResetTiers() => !IsTierResetPending;

	/// <summary>
	/// Reset to defaults: the stored table is dropped when the tab is saved (<c>resetCodexModelTiers</c>); until then the rows show what is in use and
	/// are read-only. Reload (or Discard on close) takes it back.
	/// </summary>
	[RelayCommand(CanExecute = nameof(CanResetTiers))]
	private void ResetTiers() => IsTierResetPending = true;
}
