using System;
using ReactiveUI;
using TessynDesktop.Models;
using TessynDesktop.Services;

namespace TessynDesktop.ViewModels;

/// <summary>
/// One toggle-selectable chip of the Tags tab (FR.24.11, FR.24.12). Left click toggles the selection;
/// <see cref="Open"/> backs the "Open in browser" context-menu item and reuses <see cref="SessionTagLauncher"/>,
/// so the FR.24.7 URL policy still applies.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TagSearchChipViewModel : ViewModelBase
{
	private readonly SessionTagLauncher _launcher;
	private readonly Action<TagSearchChipViewModel> _onToggle;
	private bool _isSelected;

	public TagSearchChipViewModel(SessionTagModel tag, int sessionCount, bool isSelected,
		SessionTagLauncher launcher, Action<TagSearchChipViewModel> onToggle)
	{
		_launcher = launcher;
		_onToggle = onToggle;
		_isSelected = isSelected;
		Tag = tag;
		Key = tag.Key;
		Label = tag.Label;
		Url = tag.Url;
		Tooltip = $"{tag.Url}\n{(sessionCount == 1 ? "In 1 session" : $"In {sessionCount} sessions")}";
	}

	public SessionTagModel Tag { get; }
	public string Key { get; }
	public string Label { get; }
	public string Url { get; }

	/// <summary>Line 1: the URL. Line 2: <c>In 1 session</c> / <c>In N sessions</c> (counted within scope).</summary>
	public string Tooltip { get; }

	public bool IsSelected
	{
		get => _isSelected;
		set => this.RaiseAndSetIfChanged(ref _isSelected, value);
	}

	/// <summary>Left click: asks the owner to toggle this tag's selection.</summary>
	public void Toggle() => _onToggle(this);

	public void Open() => _launcher.Open(Url);
}
