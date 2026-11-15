using CommunityToolkit.Mvvm.Input;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>One reference (FR.23.18) on a Focus item: a button that jumps to an entry, previews a file or opens a URL.</summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class FocusRefViewModel
{
	public const string EntryKind = "entry";
	public const string FileKind = "file";
	public const string UrlKind = "url";

	private readonly Action<FocusRefViewModel> _activate;

	public FocusRefViewModel(AttentionRefDto reference, Action<FocusRefViewModel> activate)
	{
		Reference = reference;
		_activate = activate;
		Label = LabelOf(reference);
		ToolTip = reference.Kind switch
		{
			EntryKind => "Jump to this place in the output",
			FileKind => reference.Path ?? reference.Name ?? string.Empty,
			UrlKind => reference.Url ?? string.Empty,
			_ => string.Empty,
		};
	}

	public AttentionRefDto Reference { get; }

	public string Kind => Reference.Kind;

	/// <summary>The posting session's label, or a fallback by kind.</summary>
	public string Label { get; }

	public string ToolTip { get; }

	/// <summary>False for a kind this client does not know (it is listed but inert).</summary>
	public bool IsKnown => Kind is EntryKind or FileKind or UrlKind;

	private static string LabelOf(AttentionRefDto reference)
	{
		if (!string.IsNullOrWhiteSpace(reference.Label))
			return reference.Label!;

		return reference.Kind switch
		{
			EntryKind => "Output",
			FileKind => reference.Name ?? reference.Path ?? "File",
			UrlKind => Uri.TryCreate(reference.Url, UriKind.Absolute, out var uri) ? uri.Host : reference.Url ?? "Link",
			_ => reference.Kind,
		};
	}

	[RelayCommand(CanExecute = nameof(IsKnown))]
	private void Open() => _activate(this);
}
