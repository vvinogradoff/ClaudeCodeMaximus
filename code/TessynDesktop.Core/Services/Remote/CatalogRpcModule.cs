using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>Remote <c>catalog.get</c>: the models, efforts and profiles a turn can be sent with (contract section 3.20).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CatalogRpcModule : IRemoteRpcModule
{
	private readonly IClaudeModelService _models;
	private readonly IAppSettingsService _appSettings;
	private readonly IProfileLabelService _labels;

	public CatalogRpcModule(IClaudeModelService models, IAppSettingsService appSettings, IProfileLabelService labels)
	{
		_models      = models;
		_appSettings = appSettings;
		_labels      = labels;
	}

	/// <inheritdoc />
	public void Register(TdcpMethodRegistry registry) =>
		registry.Add(Constants.Remote.Methods.CatalogGet, async (_, _) => await GetAsync(), remoteCallable: true);

	private async Task<object?> GetAsync()
	{
		var models = new[] { new ModelOptionDto(Constants.Remote.DefaultOptionId, "Default", null) }
			.Concat(_models.GetCachedModels().Select(m => new ModelOptionDto(m.Id, m.DisplayName, m.Provider)))
			.ToList();

		var profiles = new[] { new ProfileOptionDto(Constants.Remote.DefaultOptionId, await _labels.GetDefaultLabelAsync()) }
			.Concat(StoredProfiles().Select(p => new ProfileOptionDto(p.ProfileId, p.DisplayName)))
			.ToList();

		return new CatalogResult(models, [.. Constants.Remote.Efforts], profiles, MediaPreviewHosts());
	}

	private string[] MediaPreviewHosts()
	{
		lock (_appSettings.SyncRoot)
			return [.. _appSettings.Settings.MediaPreviewHosts.Where(h => !string.IsNullOrWhiteSpace(h)).Select(h => h.Trim().ToLowerInvariant())];
	}

	private ClaudeProfileModel[] StoredProfiles()
	{
		lock (_appSettings.SyncRoot)
			return [.. _appSettings.Settings.Profiles];
	}
}
