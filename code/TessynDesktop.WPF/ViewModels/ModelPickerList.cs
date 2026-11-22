using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// The lines of the command bar's selectors from <c>catalog.get</c> (FR.33.2, FR.33.3, FR.33.5). The model picker is Default, then the groups
/// <b>Claude</b>, <b>GPT (ChatGPT plan)</b> (Codex's default model first, or the disabled sign-in hint) and <b>Local</b>, each under a header. The headers
/// are only shown when there is more than Claude to tell apart, so a Claude-only daemon gives the same flat list as before.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class ModelPickerList
{
	/// <summary>The model picker's lines.</summary>
	public static List<SelectorOptionViewModel> Models(CatalogResult catalog)
	{
		var defaults = catalog.Models.Where(m => m.Id == Constants.TurnTexts.DefaultOption).ToList();
		var others = catalog.Models.Where(m => m.Id != Constants.TurnTexts.DefaultOption).ToList();
		var claude = others.Where(m => GroupOf(m) == Constants.Codex.ClaudeGroup).ToList();
		var gpt = others.Where(m => GroupOf(m) == Constants.Codex.GptGroup).OrderByDescending(m => m.IsDefault == true).ToList();   // stable: Codex's order otherwise
		var local = others.Where(m => GroupOf(m) == Constants.Codex.LocalGroup).ToList();
		var hint = gpt.Count == 0 && !string.IsNullOrEmpty(catalog.GptHint) ? catalog.GptHint : null;
		var grouped = gpt.Count > 0 || local.Count > 0 || hint != null;

		var lines = defaults.Select(Option).ToList();
		if (!grouped)
		{
			lines.AddRange(claude.Select(Option));
			return lines;
		}

		if (claude.Count > 0)
		{
			lines.Add(SelectorOptionViewModel.Header(Constants.Codex.ClaudeGroup));
			lines.AddRange(claude.Select(Option));
		}

		if (gpt.Count > 0 || hint != null)
		{
			lines.Add(SelectorOptionViewModel.Header(Constants.Codex.GptGroup));
			if (hint != null)
				lines.Add(SelectorOptionViewModel.Hint(Constants.Codex.GptHintId, hint));
			lines.AddRange(gpt.Select(Option));
		}

		if (local.Count > 0)
		{
			lines.Add(SelectorOptionViewModel.Header(Constants.Codex.LocalGroup));
			lines.AddRange(local.Select(Option));
		}

		return lines;
	}

	/// <summary>The global effort list (Claude's, and what a model without its own list offers).</summary>
	public static List<SelectorOptionViewModel> GlobalEfforts(CatalogResult catalog) =>
		catalog.Efforts.Select(e => new SelectorOptionViewModel(e, Capitalize(e))).ToList();

	/// <summary>Every profile of the catalog, each with its engine.</summary>
	public static List<SelectorOptionViewModel> Profiles(CatalogResult catalog) =>
		catalog.Profiles.Select(p => new SelectorOptionViewModel(p.Id, p.DisplayName, engine: p.Engine)).ToList();

	/// <summary>
	/// FR.33.3: Default and then the model's own efforts (a GPT model's with Codex's descriptions as tooltips); the global list for "default", for no
	/// model and for a model the daemon sent no list for.
	/// </summary>
	public static List<SelectorOptionViewModel> EffortsFor(SelectorOptionViewModel? model, IReadOnlyList<SelectorOptionViewModel> global)
	{
		if (model is not { IsSelectable: true, Efforts: { } own } || model.Id == Constants.TurnTexts.DefaultOption)
			return [.. global];

		var defaultOption = global.FirstOrDefault(e => e.Id == Constants.TurnTexts.DefaultOption)
		                    ?? new SelectorOptionViewModel(Constants.TurnTexts.DefaultOption, Capitalize(Constants.TurnTexts.DefaultOption));
		return [defaultOption, .. own.Where(e => e.Id != Constants.TurnTexts.DefaultOption).Select(e => new SelectorOptionViewModel(e.Id, Capitalize(e.Id), toolTip: e.Description))];
	}

	/// <summary>FR.33.5: only the profiles of the model's engine; a profile is never used across engines. No model or "default" is the Claude engine.</summary>
	public static List<SelectorOptionViewModel> ProfilesFor(SelectorOptionViewModel? model, IReadOnlyList<SelectorOptionViewModel> all)
	{
		var engine = EngineOf(model);
		return all.Where(p => p.Engine == engine).ToList();
	}

	/// <summary>The engine a turn with this model selected runs on (FR.33.4: "default" and no model are Claude).</summary>
	public static string EngineOf(SelectorOptionViewModel? model) =>
		model is { IsSelectable: true, IsCodex: true } ? RemoteProtocol.Engines.Codex : RemoteProtocol.Engines.Claude;

	private static string GroupOf(ModelOptionDto model) =>
		model.Engine == RemoteProtocol.Engines.Codex || model.Provider == ModelProvider.OpenAI ? Constants.Codex.GptGroup
		: model.Provider == ModelProvider.Ollama ? Constants.Codex.LocalGroup
		: Constants.Codex.ClaudeGroup;

	private static SelectorOptionViewModel Option(ModelOptionDto model)
	{
		var engine = model.Provider == ModelProvider.OpenAI ? RemoteProtocol.Engines.Codex : model.Engine;
		var toolTip = string.IsNullOrEmpty(model.Upgrade)
			? model.DisplayName
			: model.DisplayName + Environment.NewLine + string.Format(Constants.Codex.UpgradeFormat, model.Upgrade);
		return new SelectorOptionViewModel(model.Id, model.DisplayName, model.Provider, engine, toolTip, model.Efforts);
	}

	private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
