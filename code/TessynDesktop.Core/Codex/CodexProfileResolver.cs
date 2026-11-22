using System;
using System.Collections.Generic;
using System.Linq;
using TessynDesktop.Models;
using TessynDesktop.Models.Turns;
using TessynDesktop.Services;

namespace TessynDesktop.Codex;

/// <inheritdoc cref="ICodexProfileResolver" />
/// <remarks>
/// Also the engine's <see cref="ICodexProfileSelector"/>: a turn runs on the profile its settings name (<see cref="TurnSettings.CodexProfile"/>,
/// resolved by the pipeline before the turn lock), else on what <see cref="ResolveForTurn"/> gives. Stateless; reads the tree and the profiles
/// under the settings lock. Created by Claude.
/// </remarks>
public sealed class CodexProfileResolver : ICodexProfileResolver, ICodexProfileSelector
{
	private readonly IAppSettingsService _appSettings;
	private readonly ICodexProfileService _profiles;
	private readonly ISessionFileService? _files;
	private readonly CodexProfileNaming _naming = new();

	/// <param name="files">Tells which of a directory's ChatGPT sessions ran last; without it the first one found counts.</param>
	public CodexProfileResolver(IAppSettingsService appSettings, ICodexProfileService profiles, ISessionFileService? files = null)
	{
		_appSettings = appSettings;
		_profiles    = profiles;
		_files       = files;
	}

	/// <inheritdoc />
	public CodexProfileRef Select(SessionNodeModel node, TurnSettings? settings) =>
		settings?.CodexProfile
		?? ResolveForTurn(node).Profile
		?? new CodexProfileRef(Constants.Codex.Profiles.DefaultProfileId, CodexHome: null);

	/// <inheritdoc />
	public CodexProfileChoice ResolveForTurn(SessionNodeModel node, string? perTurnProfile = null)
	{
		if (!string.IsNullOrEmpty(perTurnProfile))
			return Find(perTurnProfile)
			       ?? new CodexProfileChoice(null, perTurnProfile, string.Format(Constants.Codex.Profiles.ClaudeProfileForGptFormat, perTurnProfile, ListNames()));

		string? pinId, pinName, supervisorId, workingDirectory;
		lock (_appSettings.SyncRoot)
		{
			pinId            = node.ProfileId;
			pinName          = node.ProfileName;
			supervisorId     = node.SupervisorNodeId;
			workingDirectory = node.WorkingDirectory;
		}

		if (_naming.IsCodexProfileId(pinId))
		{
			var pinned = Find(pinId!);
			if (pinned is null)
				return new CodexProfileChoice(null, pinName ?? pinId!, string.Format(Constants.Codex.Profiles.PinnedMissingFormat, node.Name, pinName ?? pinId));
			if (_profiles.HasAuthError(pinned.Profile!.ProfileId))
				return pinned with { Error = string.Format(Constants.Codex.Profiles.PinnedSignedOutFormat, node.Name, pinned.DisplayName) };
			return pinned;
		}

		return Inherit(supervisorId is null ? null : FindSession(supervisorId), workingDirectory);
	}

	/// <inheritdoc />
	public CodexProfileChoice Inherit(SessionNodeModel? creator, string workingDirectory)
	{
		if (creator is not null && _naming.IsCodexProfileId(creator.ProfileId) && Find(creator.ProfileId!) is { } fromCreator
		    && !_profiles.HasAuthError(fromCreator.Profile!.ProfileId))
			return fromCreator;

		if (LastUsedInDirectory(workingDirectory) is { } fromDirectory)
			return fromDirectory;

		if (CatalogProfile() is { } fromCatalog)
			return fromCatalog;

		return new CodexProfileChoice(null, Constants.Codex.Profiles.NoSignedInProfile, Constants.Codex.Profiles.NoSignedInProfile);
	}

	/// <inheritdoc />
	public CodexProfileChoice? Find(string idOrName)
	{
		var info = _profiles.List().FirstOrDefault(p =>
			string.Equals(p.ProfileId, idOrName, StringComparison.OrdinalIgnoreCase) || string.Equals(p.DisplayName, idOrName, StringComparison.OrdinalIgnoreCase));
		if (info is null || _profiles.Find(info.ProfileId) is not { } profile)
			return null;

		return new CodexProfileChoice(profile, info.DisplayName, null);
	}

	/// <inheritdoc />
	public string ListNames()
	{
		var names = _profiles.List().Where(p => p.IsKnownSignedIn).Select(p => p.DisplayName).ToList();
		return names.Count == 0 ? Constants.Codex.Profiles.NoChatGptProfilesListed : string.Join(", ", names);
	}

	/// <summary>The ChatGPT profile of the directory's Codex session that ran last (by its transcript's write time).</summary>
	private CodexProfileChoice? LastUsedInDirectory(string workingDirectory)
	{
		List<(string ProfileId, string FileName)> pinned;
		lock (_appSettings.SyncRoot)
		{
			var directory = _appSettings.Settings.Tree.FirstOrDefault(d => string.Equals(d.Path, workingDirectory, StringComparison.OrdinalIgnoreCase));
			pinned = directory is null
				? []
				: Sessions(directory.Sessions, directory.Groups)
					.Where(s => _naming.IsCodexProfileId(s.ProfileId))
					.Select(s => (s.ProfileId!, s.FileName))
					.ToList();
		}

		foreach (var (profileId, _) in pinned.OrderByDescending(p => _files?.GetLastWriteTimeUtc(p.FileName) ?? DateTimeOffset.MinValue))
		{
			if (Find(profileId) is { } choice && !_profiles.HasAuthError(choice.Profile!.ProfileId))
				return choice;
		}

		return null;
	}

	/// <summary>The catalog profile (FR.33.2) as far as it is known without asking Codex: configured, else Codex Default, else the first stored one.</summary>
	private CodexProfileChoice? CatalogProfile()
	{
		string? configured;
		lock (_appSettings.SyncRoot)
			configured = _appSettings.Settings.CodexCatalogProfileId;

		var signedIn = _profiles.List().Where(p => p.IsKnownSignedIn && !p.HasAuthError).ToList();
		var pick = signedIn.FirstOrDefault(p => string.Equals(p.ProfileId, configured, StringComparison.OrdinalIgnoreCase))
		           ?? signedIn.FirstOrDefault(p => p.IsDefault)
		           ?? signedIn.FirstOrDefault();
		return pick is null ? null : Find(pick.ProfileId);
	}

	private SessionNodeModel? FindSession(string nodeId)
	{
		lock (_appSettings.SyncRoot)
			return _appSettings.Settings.Tree.SelectMany(d => Sessions(d.Sessions, d.Groups)).FirstOrDefault(s => s.NodeId == nodeId);
	}

	private static IEnumerable<SessionNodeModel> Sessions(List<SessionNodeModel> sessions, List<GroupNodeModel> groups) =>
		sessions.Concat(groups.SelectMany(g => Sessions(g.Sessions, g.Groups)));
}
