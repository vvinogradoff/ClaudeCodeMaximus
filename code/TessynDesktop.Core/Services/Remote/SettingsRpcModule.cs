using System.Text.Json.Nodes;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// <c>settings.sections</c>, <c>settings.get</c> and <c>settings.update</c> (package J3): the daemon-owned settings as typed sections with a version
/// per section. Loopback only: the values hold local paths and the Telegram chat and user ids.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SettingsRpcModule : IRemoteRpcModule
{
	private readonly ISettingsSectionService _sections;

	public SettingsRpcModule(ISettingsSectionService sections)
	{
		_sections = sections;
	}

	/// <inheritdoc />
	public void Register(TdcpMethodRegistry registry)
	{
		registry.AddSync(RemoteProtocol.SettingsApi.SectionsMethod, _ => new SettingsSectionsResult(_sections.GetSections()), remoteCallable: false);
		registry.AddSync(RemoteProtocol.SettingsApi.GetMethod, Get, remoteCallable: false);
		registry.AddSync(RemoteProtocol.SettingsApi.UpdateMethod, Update, remoteCallable: false);
	}

	private object? Get(JsonNode? @params) => _sections.Get(TdcpParams.RequireString(@params, "section"));

	private object? Update(JsonNode? @params)
	{
		var section     = TdcpParams.RequireString(@params, "section");
		var patch       = TdcpParams.Find(@params, "patch") ?? throw new TdcpInvalidParamsException("'patch' is required");
		var baseVersion = TdcpParams.OptionalLong(@params, "baseVersion") ?? throw new TdcpInvalidParamsException("'baseVersion' is required");

		return new SettingsUpdateResult(_sections.Update(section, patch, baseVersion));
	}
}
