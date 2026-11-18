using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.ViewModels.Settings;

/// <summary>
/// The Previews tab (FR.29.2, FR.27.8): the ordered preview groups (name, patterns, auto-close) with add, remove, move and reset, saved with the window's Save button, and two actions
/// that act at once after a question: forget the remembered window positions, clear the preview browser's data. While nothing is edited the list follows the stored groups
/// (a window's Auto-close button changes them).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class PreviewsSectionViewModel : SettingsSectionViewModel, ISettingsEditor, IDisposable
{
	private readonly IPreviewSettingsService _service;
	private readonly PreviewGroupMatcher _matcher;
	private readonly IBrowserDataCleaner? _cleaner;
	private readonly IDialogService _dialogs;
	private readonly IUiDispatcher? _ui;

	/// <summary>The stored groups the rows were last read from or saved as: what "edited" is measured against when the stored groups change underneath.</summary>
	private IReadOnlyList<PreviewGroupModel> _baseline = [];

	/// <summary>The row added last, so the view can put the focus into its name box.</summary>
	[ObservableProperty]
	private PreviewGroupRowViewModel? _newGroup;

	public PreviewsSectionViewModel(IPreviewSettingsService service, PreviewGroupMatcher matcher, IBrowserDataCleaner? cleaner, IDialogService dialogs, IUiDispatcher? ui = null)
	{
		_service = service;
		_matcher = matcher;
		_cleaner = cleaner;
		_dialogs = dialogs;
		_ui = ui;
		Read();
		_service.GroupsChanged += OnGroupsChanged;
	}

	public override string Title => Constants.Settings.PreviewsTitle;

	public string Help => Constants.Settings.PreviewsHelp;

	public ObservableCollection<PreviewGroupRowViewModel> Groups { get; } = [];

	public bool HasChanges => DiffersFrom(_service.Groups);

	/// <summary>Adds an empty group at the end.</summary>
	[RelayCommand]
	public void AddGroup()
	{
		var row = new PreviewGroupRowViewModel(Guid.NewGuid().ToString("N"), string.Empty, string.Empty, false);
		Groups.Add(row);
		NewGroup = row;
	}

	[RelayCommand]
	public void Remove(PreviewGroupRowViewModel? row)
	{
		if (row != null)
			Groups.Remove(row);
	}

	[RelayCommand]
	public void MoveUp(PreviewGroupRowViewModel? row) => Move(row, -1);

	[RelayCommand]
	public void MoveDown(PreviewGroupRowViewModel? row) => Move(row, 1);

	/// <summary>The rows become the default groups; stored with Save like any edit.</summary>
	[RelayCommand]
	public void ResetToDefaults()
	{
		Groups.Clear();
		foreach (var group in Constants.Previews.DefaultGroups)
			Groups.Add(new PreviewGroupRowViewModel(group.Id, group.Name, group.Patterns, false));
	}

	[RelayCommand]
	public async Task ForgetPositionsAsync()
	{
		ClearMessages();
		if (!await _dialogs.ConfirmAsync(new ConfirmDialogRequest(Constants.Settings.PreviewForgetTitle, Constants.Settings.PreviewForgetMessage, Constants.Settings.PreviewForgetConfirm, IsDestructive: true)))
			return;

		_service.ForgetPlacements();
		StatusText = Constants.Settings.PreviewForgotten;
	}

	[RelayCommand]
	public async Task ClearBrowserDataAsync()
	{
		ClearMessages();
		if (!await _dialogs.ConfirmAsync(new ConfirmDialogRequest(Constants.Settings.PreviewClearTitle, Constants.Settings.PreviewClearMessage, Constants.Settings.PreviewClearConfirm, IsDestructive: true)))
			return;

		if (_cleaner == null)
		{
			ErrorText = Constants.Settings.PreviewClearUnavailable;
			return;
		}

		if (await _cleaner.ClearAsync())
			StatusText = Constants.Settings.PreviewCleared;
		else
			ErrorText = Constants.Settings.PreviewClearFailed;
	}

	public Task<bool> SaveAsync()
	{
		ClearMessages();
		var error = Validate();
		if (error != null)
		{
			ErrorText = string.Format(Constants.Settings.SaveFailedFormat, error);
			return Task.FromResult(false);
		}

		_service.SaveGroups(Groups.Select(g => g.ToModel()).ToList());
		_baseline = _service.Groups;
		StatusText = Constants.Settings.Saved;
		return Task.FromResult(true);
	}

	public Task ReloadAsync()
	{
		ClearMessages();
		Read();
		return Task.CompletedTask;
	}

	public void Dispose() => _service.GroupsChanged -= OnGroupsChanged;

	/// <summary>The message of the first problem (every faulty row also shows its own); null when the groups can be stored.</summary>
	private string? Validate()
	{
		string? first = null;
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var row in Groups)
		{
			row.Error = RowError(row, row.Name.Trim(), seen);
			first ??= row.Error;
		}

		return first;
	}

	private string? RowError(PreviewGroupRowViewModel row, string name, HashSet<string> seen)
	{
		if (name.Length == 0)
			return Constants.Settings.PreviewGroupNameRequired;
		if (!seen.Add(name))
			return string.Format(Constants.Settings.PreviewGroupNameDuplicateFormat, name);

		var parsed = _matcher.Parse(row.Patterns);
		if (parsed.Invalid.Count > 0)
			return string.Format(Constants.Settings.PreviewGroupInvalidPatternFormat, name, parsed.Invalid[0]);

		return parsed.Patterns.Count == 0 ? string.Format(Constants.Settings.PreviewGroupNoPatternsFormat, name) : null;
	}

	private void Move(PreviewGroupRowViewModel? row, int offset)
	{
		var index = row == null ? -1 : Groups.IndexOf(row);
		var target = index + offset;
		if (index >= 0 && target >= 0 && target < Groups.Count)
			Groups.Move(index, target);
	}

	private bool DiffersFrom(IReadOnlyList<PreviewGroupModel> groups)
	{
		if (groups.Count != Groups.Count)
			return true;

		for (var i = 0; i < groups.Count; i++)
		{
			var row = Groups[i];
			if (groups[i].Id != row.Id || groups[i].Name != row.Name.Trim() || groups[i].Patterns != row.Patterns.Trim() || groups[i].AutoClose != row.AutoClose)
				return true;
		}

		return false;
	}

	private void Read()
	{
		_baseline = _service.Groups;
		Groups.Clear();
		foreach (var group in _baseline)
			Groups.Add(new PreviewGroupRowViewModel(group));
	}

	/// <summary>
	/// The tab has unsaved edits and a window's Auto-close button changed the stored groups: every row whose Auto-close the user has not changed takes the stored
	/// value, and so does the baseline, so that Save never writes a stale switch back.
	/// </summary>
	private void MergeAutoClose()
	{
		var stored = _service.Groups.ToDictionary(g => g.Id);
		var baseline = new List<PreviewGroupModel>();
		foreach (var old in _baseline)
		{
			var merged = old.Clone();
			if (stored.TryGetValue(old.Id, out var current))
			{
				var row = Groups.FirstOrDefault(r => r.Id == old.Id);
				if (row != null && row.AutoClose == old.AutoClose)
					row.AutoClose = current.AutoClose;

				merged.AutoClose = current.AutoClose;
			}

			baseline.Add(merged);
		}

		_baseline = baseline;
	}

	private void OnGroupsChanged(object? sender, EventArgs e)
	{
		void Reload()
		{
			if (!DiffersFrom(_baseline))
				Read();
			else
				MergeAutoClose();
		}

		if (_ui == null)
			Reload();
		else
			_ui.Post(Reload);
	}
}
