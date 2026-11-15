using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>One suggestion of the autocomplete popup: the label split around the typed query so the match can be shown bold.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class AutocompleteItemViewModel
{
	public AutocompleteItemViewModel(AutocompleteItemDto dto)
	{
		InsertText = dto.InsertText;
		Kind = dto.Kind;
		Detail = dto.Detail;
		Label = dto.Label;

		var start = Math.Clamp(dto.MatchStart, 0, dto.Label.Length);
		var length = Math.Clamp(dto.MatchLength, 0, dto.Label.Length - start);
		Before = dto.Label[..start];
		Match = dto.Label.Substring(start, length);
		After = dto.Label[(start + length)..];
	}

	/// <summary>What the list shows.</summary>
	public string Label { get; }

	/// <summary>What replaces the trigger text when the suggestion is accepted.</summary>
	public string InsertText { get; }

	/// <summary><c>class</c>, <c>method</c>, ... for a symbol; <c>file</c> or <c>directory</c> otherwise.</summary>
	public string Kind { get; }

	/// <summary>The relative path of a file, the namespace of a symbol, the extension of a path entry.</summary>
	public string? Detail { get; }

	public string Before { get; }

	public string Match { get; }

	public string After { get; }

	/// <summary>What assistive technology reads for the row.</summary>
	public override string ToString() => Label;

	/// <summary>A directory ends in a separator: accepting it continues the path, so the popup asks again.</summary>
	public bool IsDirectory => Kind == "directory";
}
