using System;
using System.Linq;
using TessynDesktop.Codex;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// GPT sessions in the orchestration tools (FR.33.15): a GPT id of the catalog makes a Codex session on a ChatGPT profile (the inherit rule of
/// FR.33.5, same engine only); a GPT-looking id that is not in the catalog is refused with the list of GPT models; tiers use the GPT table
/// (FR.33.14).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class AgentMcpServer
{
	/// <summary>The engine a new worker or session gets from its model, with its ChatGPT profile when it is a GPT one.</summary>
	private sealed record ChildEngine(bool IsGpt, CodexProfileChoice? Profile);

	/// <summary>FR.33.15: a stale GPT id fails loudly instead of running on Claude. Other ids keep today's behaviour.</summary>
	private void ValidateGptModel(string? model)
	{
		if (_sessionModels is null || string.IsNullOrWhiteSpace(model))
			return;

		if (_sessionModels.LooksLikeGpt(model) && !_sessionModels.IsGpt(model))
			throw new McpToolException(string.Format(Constants.Codex.Catalog.NotInCatalogFormat, model, _sessionModels.ListGptModels()));
	}

	/// <summary>True when the model is a GPT model of the catalog.</summary>
	private bool IsGptModel(string? model) => _sessionModels?.IsGpt(model) == true;

	/// <summary>
	/// Decides, before anything is created, the engine of a child on <paramref name="model"/>: for a GPT model the ChatGPT profile from the inherit
	/// rule (the creator's ChatGPT profile, else the directory's last-used one, else the catalog profile), or the FR.33.5 error.
	/// </summary>
	private ChildEngine ResolveChildEngine(SessionNodeModel creator, string? model, string workingDirectory)
	{
		ValidateGptModel(model);
		if (!IsGptModel(model) || _codexProfiles is null)
			return new ChildEngine(false, null);

		var choice = _codexProfiles.Inherit(creator, workingDirectory);
		if (!choice.IsResolved)
			throw new McpToolException(choice.Error!);

		return new ChildEngine(true, choice);
	}

	/// <summary>Gives a new child its model, engine and profile pin: a GPT child the ChatGPT pin, a Claude child the creator's Claude profile (ISSUE-002).</summary>
	private void ApplyChildEngine(SessionNodeModel child, SessionNodeModel creator, string? model, ChildEngine engine)
	{
		if (engine.IsGpt)
		{
			child.EngineId    = Constants.SessionEngines.Codex;
			child.ProfileId   = engine.Profile!.Profile!.ProfileId;
			child.ProfileName = engine.Profile.DisplayName;
		}
		else
			InheritSupervisorProfile(child, creator);

		if (!string.IsNullOrEmpty(model))
			child.ModelId = model;
	}

	/// <summary>True when the session's pin or recorded profile is a ChatGPT one (it never stands in for a Claude pin).</summary>
	private bool HasChatGptProfile(SessionNodeModel node) => _codexNaming.IsCodexProfile(node.ProfileId) || _codexNaming.IsChatGptName(node.ProfileName);

	/// <summary>
	/// <c>create_session</c>'s profile for a GPT model (FR.33.15): the given ChatGPT profile (id or name), else the inherit rule. A Claude profile
	/// is the cross-engine error.
	/// </summary>
	private ProfilePin ResolveCreatedSessionCodexProfile(string? profile, SessionNodeModel callerNode, string workingDirectory)
	{
		if (_codexProfiles is null)
			throw new McpToolException(Constants.Codex.Profiles.NoSignedInProfile);

		if (string.IsNullOrEmpty(profile))
		{
			var inherited = _codexProfiles.Inherit(callerNode, workingDirectory);
			return inherited.IsResolved
				? new ProfilePin(inherited.Profile!.ProfileId, inherited.DisplayName)
				: throw new McpToolException(inherited.Error!);
		}

		if (_codexProfiles.Find(profile) is { } chosen)
			return new ProfilePin(chosen.Profile!.ProfileId, chosen.DisplayName);

		var claude = _profiles?.FindById(profile) ?? _profiles?.FindByName(profile);
		throw new McpToolException(claude is not null
			? string.Format(Constants.Codex.Profiles.ClaudeProfileForGptFormat, claude.IsDefault ? Constants.ProfileSwitch.DefaultProfileId : claude.DisplayName, _codexProfiles.ListNames())
			: string.Format(Constants.CreateSession.UnknownProfileFormat, profile, _codexProfiles.ListNames()));
	}

	/// <summary>The efforts of a GPT model for <c>create_session</c> (FR.33.3): Default or one the model offers; anything else lists its efforts.</summary>
	private string? ValidateGptEffort(string modelId, string? effort)
	{
		if (string.IsNullOrEmpty(effort) || string.Equals(effort, Constants.Remote.DefaultOptionId, StringComparison.OrdinalIgnoreCase))
			return null;

		var offered = _sessionModels!.EffortsFor(modelId);
		return offered.FirstOrDefault(e => string.Equals(e.Effort, effort, StringComparison.OrdinalIgnoreCase))?.Effort
		       ?? throw new McpToolException(string.Format(Constants.Codex.Catalog.UnknownEffortFormat, effort, modelId, string.Join(", ", offered.Select(e => e.Effort))));
	}

	private int TierOf(string? modelId) => _tiers?.GetTier(modelId) ?? ModelTierService.GetTier(modelId);

	private string TierName(int tier) => _tiers?.TierName(tier) ?? ModelTierService.TierName(tier);
}
