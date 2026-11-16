using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>One Claude session in the import list. An empty or already imported one is greyed and cannot be selected; its title may arrive later (<c>import.titles</c>).</summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class ImportItemViewModel : ObservableObject
{
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(DisplayTitle), nameof(HasTitle), nameof(Subtitle))]
	private string? _title;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsSelectable), nameof(IsGreyed), nameof(BadgeText), nameof(HasBadge))]
	private bool _alreadyImported;

	[ObservableProperty]
	private bool _isSelected;

	public ImportItemViewModel(ImportCandidateDto candidate)
		: this(candidate.ExternalId, candidate.Title, candidate.FirstPrompt, candidate.LastModified, candidate.EntryCount, candidate.Empty, candidate.AlreadyImported, candidate.ProjectSlug, null)
	{
	}

	/// <summary>A hit of the global search: the matching message stands where the first prompt does.</summary>
	public ImportItemViewModel(SearchHitDto hit)
		: this(hit.ExternalId, hit.Title, hit.Snippet, null, 0, false, hit.ImportedNodeId != null, hit.ProjectSlug, hit.ImportedNodeId)
	{
	}

	private ImportItemViewModel(string externalId, string? title, string? firstPrompt, DateTimeOffset? lastModified, int entryCount, bool empty, bool alreadyImported, string projectSlug, string? importedNodeId)
	{
		ExternalId = externalId;
		_title = title;
		FirstPrompt = firstPrompt?.Trim() ?? string.Empty;
		LastModified = lastModified;
		EntryCount = entryCount;
		IsEmpty = empty;
		_alreadyImported = alreadyImported;
		ProjectSlug = projectSlug;
		ImportedNodeId = importedNodeId;
	}

	/// <summary>The Claude session id.</summary>
	public string ExternalId { get; }

	public string FirstPrompt { get; }

	public DateTimeOffset? LastModified { get; }

	public int EntryCount { get; }

	/// <summary>The session has no message and cannot be imported.</summary>
	public bool IsEmpty { get; }

	/// <summary>The Claude project the session lives under (a lossy form of its folder, never a path).</summary>
	public string ProjectSlug { get; }

	/// <summary>For a hit that is already in the tree, its node.</summary>
	public string? ImportedNodeId { get; }

	public bool HasTitle => !string.IsNullOrWhiteSpace(Title);

	/// <summary>The title, else the first line of the first prompt, else a placeholder.</summary>
	public string DisplayTitle => HasTitle ? Title!.Trim() : FirstLine(FirstPrompt) is { Length: > 0 } line ? line : Constants.Import.Untitled;

	public bool IsSelectable => !IsEmpty && !AlreadyImported;

	public bool IsGreyed => !IsSelectable;

	public string BadgeText => AlreadyImported ? Constants.Import.AlreadyImported : IsEmpty ? Constants.Import.EmptySession : string.Empty;

	public bool HasBadge => BadgeText.Length > 0;

	/// <summary>Date and size line of the row: when the session was used and how many messages it has.</summary>
	public string DetailText
	{
		get
		{
			var parts = new List<string>();
			if (LastModified is { } at)
				parts.Add(at.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
			if (EntryCount > 0)
				parts.Add(EntryCount + " messages");
			return string.Join(" · ", parts);
		}
	}

	/// <summary>What the row shows under the title: the first prompt (the matching message for a hit), unless it is the title itself.</summary>
	public string Subtitle => HasTitle && FirstLine(FirstPrompt) != DisplayTitle ? FirstLine(FirstPrompt) : string.Empty;

	public string AutomationName => Constants.Import.SelectPrefix + ExternalId;

	private static string FirstLine(string text) => text.Split('\n', 2)[0].Trim();
}
