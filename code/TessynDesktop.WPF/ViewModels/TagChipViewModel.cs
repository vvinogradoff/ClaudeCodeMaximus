using CommunityToolkit.Mvvm.ComponentModel;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>A chip of the Tags tab: a tag with the number of sessions carrying it; clicking toggles it in the OR filter. Updated in place by a new search.</summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class TagChipViewModel : ObservableObject
{
	[ObservableProperty]
	private bool _selected;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsStub), nameof(Text), nameof(ToolTipText))]
	private int _sessionCount;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(Text), nameof(AutomationName), nameof(ToolTipText))]
	private string _label;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(ToolTipText), nameof(HasUrl))]
	private string _url;

	public TagChipViewModel(TagChipDto chip)
	{
		Key = chip.Key;
		Kind = chip.Kind;
		_label = chip.Label;
		_url = chip.Url;
		_sessionCount = chip.SessionCount;
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

	public SessionTagKind Kind { get; }

	public string KindText { get; }

	/// <summary>The selected tag has left the index: the chip stays so the user can deselect it.</summary>
	public bool IsStub => SessionCount == 0;

	public bool HasUrl => !string.IsNullOrEmpty(Url);

	/// <summary>The text of the chip: label and count.</summary>
	public string Text => IsStub ? Label : $"{Label} ({SessionCount})";

	/// <summary>The URL, then how many sessions carry the tag (FR.24.12).</summary>
	public string ToolTipText => IsStub ? Url : string.IsNullOrEmpty(Url) ? $"In {SessionCount} sessions" : $"{Url}{Environment.NewLine}In {SessionCount} sessions";

	public string AutomationName => Constants.SessionLists.TagChipPrefix + Label;

	/// <summary>Takes the new search's data for this tag.</summary>
	public void Update(TagChipDto chip)
	{
		Label = chip.Label;
		Url = chip.Url;
		SessionCount = chip.SessionCount;
		Selected = chip.Selected;
	}

	public TagRefDto ToRef() => new(Key, Label, Url, Kind);
}
