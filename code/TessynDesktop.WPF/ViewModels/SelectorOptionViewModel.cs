using TessynDesktop.Models;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>One entry of a command-bar selector (model, effort or profile): the id sent to the daemon and how it is shown.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SelectorOptionViewModel
{
	public SelectorOptionViewModel(string id, string label, ModelProvider? provider = null)
	{
		Id = id;
		Provider = provider;
		Label = label;
		IsSystem = label.StartsWith(Constants.TurnTexts.SystemMarker, StringComparison.Ordinal);
		ShortLabel = label.Length <= Constants.TurnLimits.SelectorLabelChars
			? label
			: Constants.TurnTexts.TruncationMark + label[^(Constants.TurnLimits.SelectorLabelChars - Constants.TurnTexts.TruncationMark.Length)..];
	}

	/// <summary>"default" or a model id, an effort name, a profile id: what the daemon expects.</summary>
	public string Id { get; }

	public string Label { get; }

	/// <summary>Where a model runs; null for effort and profile options and for the "default" model.</summary>
	public ModelProvider? Provider { get; }

	/// <summary>The label with its start cut when long: the end of a model name tells models apart (the full label is the tooltip).</summary>
	public string ShortLabel { get; }

	/// <summary>A system profile ("[Sys] ...", not one of the user's own): shown dimmed.</summary>
	public bool IsSystem { get; }
}
