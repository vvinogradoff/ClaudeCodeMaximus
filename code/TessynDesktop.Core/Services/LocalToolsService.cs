using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <inheritdoc cref="ILocalToolsService" />
/// <remarks>Created by Claude</remarks>
public sealed class LocalToolsService : ILocalToolsService
{
	private readonly ISessionRegistry _registry;
	private readonly IAppSettingsService _appSettings;

	public LocalToolsService(ISessionRegistry registry, IAppSettingsService appSettings)
	{
		_registry    = registry;
		_appSettings = appSettings;
	}

	/// <inheritdoc />
	public LocalToolsDto Get(string directoryId)
	{
		var directory = _registry.FindDirectoryByNodeId(directoryId) ?? throw new TdcpException(Constants.TreeMutation.DirectoryNotFound);

		lock (_appSettings.SyncRoot)
			return Build(directory);
	}

	/// <inheritdoc />
	public LocalToolsDto Set(string directoryId, bool enabled, IReadOnlyList<string> nativeTools, bool includeCmxMcp, IReadOnlyList<string> includedMcpServerIds)
	{
		var directory = _registry.FindDirectoryByNodeId(directoryId) ?? throw new TdcpException(Constants.TreeMutation.DirectoryNotFound);

		foreach (var tool in nativeTools)
		{
			if (!Constants.LocalTools.Catalog.Contains(tool))
				throw new TdcpInvalidParamsException($"{Constants.TreeMutation.UnknownNativeTool}: {tool}");
		}

		LocalToolsDto result;
		lock (_appSettings.SyncRoot)
		{
			var registered = _appSettings.Settings.ExternalMcpServers.Select(s => s.Id).ToHashSet();
			directory.LocalToolProfile = new LocalToolProfileModel
			{
				Enabled                      = enabled,
				NativeTools                  = [.. nativeTools.Distinct()],
				IncludeCmxMcp                = includeCmxMcp,
				IncludedExternalMcpServerIds = [.. includedMcpServerIds.Where(registered.Contains).Distinct()],
			};
			_appSettings.Save();
			result = Build(directory);
		}

		_registry.RaiseTreeModified();
		return result;
	}

	/// <summary>The caller holds the settings lock.</summary>
	private LocalToolsDto Build(DirectoryNodeModel directory)
	{
		var servers = _appSettings.Settings.ExternalMcpServers;
		var profile = directory.LocalToolProfile;
		var known   = servers.Select(s => s.Id).ToHashSet();

		return new LocalToolsDto(
			profile?.Enabled ?? true,
			[.. profile?.NativeTools ?? [.. Constants.LocalTools.DefaultNativeTools]],
			profile?.IncludeCmxMcp ?? false,
			[.. (profile?.IncludedExternalMcpServerIds ?? []).Where(known.Contains)],
			[.. Constants.LocalTools.Catalog],
			[.. servers.Select(s => new LocalToolsMcpServerDto(s.Id, s.DisplayName))]);
	}
}
