using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>
/// The catalog of fixture C (TC.CX.01) as <c>catalog.get</c> sends it: Claude models, the GPT models of the fake app-server (<c>gpt-x-sol</c> default with
/// low to xhigh, <c>gpt-x-astra</c> low to max, <c>gpt-x-luna</c> low and medium) listed out of order to prove the default goes first, a local model,
/// and the Claude and ChatGPT profiles.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class CodexCatalogs
{
	public const string ChatGptProfile = "codex_1";
	public const string ChatGptName = "ChatGPT · u@example.com";

	public static readonly ModelEffortDto[] ClaudeEfforts = [new("max"), new("high"), new("medium"), new("low")];

	public static CatalogResult FixtureC(string? gptHint = null, bool withGpt = true)
	{
		var models = new List<ModelOptionDto>
		{
			new("default", "Default", null),
			new("claude-opus-4-8", "Opus 4.8", ModelProvider.Anthropic, RemoteProtocol.Engines.Claude, ClaudeEfforts),
		};
		var profiles = new List<ProfileOptionDto> { new("default", "[Sys] Default"), new("profile_1", "Work") };
		if (withGpt)
		{
			models.Add(new("gpt-x-astra", "GPT-X Astra", ModelProvider.OpenAI, RemoteProtocol.Engines.Codex,
				[new("low", "Fast"), new("medium", "Balanced"), new("high", "Deep"), new("max", "Deepest")], Upgrade: "gpt-x-astra-2"));
			models.Add(new("gpt-x-sol", "GPT-X Sol", ModelProvider.OpenAI, RemoteProtocol.Engines.Codex,
				[new("low", "Fast answers"), new("medium", "Balanced"), new("high", "Thorough"), new("xhigh", "Most thorough")], IsDefault: true));
			models.Add(new("gpt-x-luna", "GPT-X Luna", ModelProvider.OpenAI, RemoteProtocol.Engines.Codex, [new("low", "Fast"), new("medium", "Balanced")]));
			profiles.Add(new(ChatGptProfile, ChatGptName, RemoteProtocol.Engines.Codex, "plus"));
		}

		models.Add(new("qwen3", "Qwen 3", ModelProvider.Ollama, RemoteProtocol.Engines.Claude, ClaudeEfforts));
		return new CatalogResult(models, ["default", "max", "high", "medium", "low"], profiles, GptHint: gptHint);
	}

	/// <summary>A Claude-only catalog, as a daemon without Codex sends it.</summary>
	public static CatalogResult ClaudeOnly() => new(
		[new ModelOptionDto("default", "Default", null), new ModelOptionDto("claude-opus-4-8", "Opus 4.8", ModelProvider.Anthropic, RemoteProtocol.Engines.Claude, ClaudeEfforts)],
		["default", "max", "high", "medium", "low"],
		[new ProfileOptionDto("default", "[Sys] Default")]);
}
