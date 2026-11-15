using CommunityToolkit.Mvvm.ComponentModel;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>A chip of the Tags tab: a tag with the number of sessions carrying it; clicking toggles it in the OR filter.</summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class TagChipViewModel : ObservableObject
{
	[ObservableProperty]
	private bool _selected;

	public TagChipViewModel(TagChipDto chip)
	{
		Key = chip.Key;
		Label = chip.Label;
		Url = chip.Url;
		Kind = chip.Kind;
		SessionCount = chip.SessionCount;
		_selected = chip.Selected;
		KindText = chip.Kind switch
		{
			SessionTagKind.LinearIssue => "Linear",
			SessionTagKind.GitHubPullRequest => "PR",
			SessionTagKind.GitHubRepository => "Repo",
			_ => string.Empty,
		};
	}

	public string Key { get; }

	public string Label { get; }

	public string Url { get; }

	public SessionTagKind Kind { get; }

	public string KindText { get; }

	public int SessionCount { get; }

	/// <summary>The selected tag has left the index: the chip stays so the user can deselect it.</summary>
	public bool IsStub => SessionCount == 0;

	/// <summary>The text of the chip: label and count.</summary>
	public string Text => IsStub ? Label : $"{Label} ({SessionCount})";

	public string AutomationName => Constants.SessionLists.TagChipPrefix + Label;

	public TagRefDto ToRef() => new(Key, Label, Url, Kind);
}
