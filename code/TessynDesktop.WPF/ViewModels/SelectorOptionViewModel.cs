using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>One entry of a command-bar selector (model, effort or profile): the id sent to the daemon and how it is shown.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SelectorOptionViewModel
{
	public SelectorOptionViewModel(string id, string label, ModelProvider? provider = null, string? engine = null, string? toolTip = null,
		IReadOnlyList<ModelEffortDto>? efforts = null, SelectorOptionKind kind = SelectorOptionKind.Option)
	{
		Id = id;
		Provider = provider;
		Label = label;
		Engine = string.IsNullOrEmpty(engine) ? RemoteProtocol.Engines.Claude : engine;
		ToolTip = string.IsNullOrEmpty(toolTip) ? label : toolTip;
		Efforts = efforts;
		Kind = kind;
		IsSystem = label.StartsWith(Constants.TurnTexts.SystemMarker, StringComparison.Ordinal);
		ShortLabel = kind != SelectorOptionKind.Option || label.Length <= Constants.TurnLimits.SelectorLabelChars
			? label
			: Constants.TurnTexts.TruncationMark + label[^(Constants.TurnLimits.SelectorLabelChars - Constants.TurnTexts.TruncationMark.Length)..];
	}

	/// <summary>A group header of the model picker.</summary>
	public static SelectorOptionViewModel Header(string title) => new(Constants.Codex.HeaderIdPrefix + title, title, kind: SelectorOptionKind.Header);

	/// <summary>A disabled explanation line.</summary>
	public static SelectorOptionViewModel Hint(string id, string text) => new(id, text, kind: SelectorOptionKind.Hint);

	/// <summary>"default" or a model id, an effort name, a profile id: what the daemon expects.</summary>
	public string Id { get; }

	public string Label { get; }

	/// <summary>Where a model runs; null for effort and profile options and for the "default" model.</summary>
	public ModelProvider? Provider { get; }

	/// <summary>The session engine of a model or a profile (<c>claude</c> or <c>codex</c>, FR.33.1); <c>claude</c> for efforts and when the daemon sent none.</summary>
	public string Engine { get; }

	public bool IsCodex => Engine == RemoteProtocol.Engines.Codex;

	/// <summary>The tooltip: the full label, plus Codex's upgrade hint of a GPT model or Codex's description of a GPT effort.</summary>
	public string ToolTip { get; }

	/// <summary>A model's own efforts without "default" (FR.33.3); null when the daemon sent none, so the global list applies.</summary>
	public IReadOnlyList<ModelEffortDto>? Efforts { get; }

	public SelectorOptionKind Kind { get; }

	/// <summary>Only options can be picked; headers and hints are shown disabled.</summary>
	public bool IsSelectable => Kind == SelectorOptionKind.Option;

	public bool IsHeader => Kind == SelectorOptionKind.Header;

	public bool IsHint => Kind == SelectorOptionKind.Hint;

	/// <summary>The label with its start cut when long: the end of a model name tells models apart (the full label is the tooltip).</summary>
	public string ShortLabel { get; }

	/// <summary>A system profile ("[Sys] ...", not one of the user's own): shown dimmed.</summary>
	public bool IsSystem { get; }

	/// <summary>The label: UI Automation (screen readers, the E2E tests) names a selector's items by it instead of by the type name.</summary>
	public override string ToString() => Label;
}
