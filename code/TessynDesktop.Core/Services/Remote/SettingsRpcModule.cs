using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
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
		registry.Add(RemoteProtocol.SettingsApi.GetMethod, async (p, ct) => await GetAsync(p, ct), remoteCallable: false);
		registry.AddSync(RemoteProtocol.SettingsApi.UpdateMethod, Update, remoteCallable: false);
	}

	/// <summary>The section with its live values (the Codex status of <c>general</c>, FR.33.17).</summary>
	private async Task<object?> GetAsync(JsonNode? @params, CancellationToken cancellationToken) =>
		await _sections.GetAsync(TdcpParams.RequireString(@params, "section"), cancellationToken);

	private object? Update(JsonNode? @params)
	{
		var section     = TdcpParams.RequireString(@params, "section");
		var patch       = TdcpParams.Find(@params, "patch") ?? throw new TdcpInvalidParamsException("'patch' is required");
		var baseVersion = TdcpParams.OptionalLong(@params, "baseVersion") ?? throw new TdcpInvalidParamsException("'baseVersion' is required");

		return new SettingsUpdateResult(_sections.Update(section, patch, baseVersion));
	}
}
