using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using TessynDesktop.Codex.Protocol;
using TessynDesktop.Models;

namespace TessynDesktop.Codex;

/// <inheritdoc cref="ICodexModelCatalog" />
/// <remarks>
/// The cache is <c>%AppData%\TessynDesktop\codex-models.json</c>. A refresh reads every page of <c>model/list</c> with <c>includeHidden: false</c>
/// (hidden entries are dropped as well) on the profile <see cref="ICodexProfileService.ResolveCatalogProfileAsync"/> picks, which is always a
/// signed-in one. A sign-in refreshes the list. Created by Claude.
/// </remarks>
public sealed class CodexModelCatalog : ICodexModelCatalog
{
	private static readonly ILogger _log = Log.ForContext<CodexModelCatalog>();

	private readonly CodexHostPool _pool;
	private readonly ICodexProfileService _profiles;
	private readonly TimeProvider _time;
	private readonly string _cachePath;
	private readonly SemaphoreSlim _refreshGate = new(1, 1);
	private CodexModelCacheModel? _cache;

	/// <param name="cacheDirectory">Where <c>codex-models.json</c> is; the data root when null.</param>
	public CodexModelCatalog(CodexHostPool pool, ICodexProfileService profiles, TimeProvider? time = null, string? cacheDirectory = null)
	{
		_pool      = pool;
		_profiles  = profiles;
		_time      = time ?? TimeProvider.System;
		_cachePath = Path.Combine(cacheDirectory ?? Constants.DataRoot, Constants.Codex.Catalog.CacheFileName);

		_profiles.SignedIn += OnSignedIn;
	}

	/// <inheritdoc />
	public event EventHandler? Updated;

	/// <inheritdoc />
	public IReadOnlyList<CodexCatalogModel> Models => Volatile.Read(ref _cache)?.Models ?? [];

	/// <inheritdoc />
	public bool IsLoaded => Volatile.Read(ref _cache) is not null;

	/// <inheritdoc />
	public DateTimeOffset? FetchedAtUtc => Volatile.Read(ref _cache)?.FetchedAtUtc;

	/// <inheritdoc />
	public string? SourceProfileId => Volatile.Read(ref _cache)?.SourceProfileId;

	/// <inheritdoc />
	public CodexCatalogModel? Find(string? modelId) =>
		string.IsNullOrEmpty(modelId) ? null : Models.FirstOrDefault(m => string.Equals(m.Id, modelId, StringComparison.OrdinalIgnoreCase));

	/// <inheritdoc />
	public async Task StartAsync(CancellationToken cancellationToken = default)
	{
		LoadCache();
		var cache = Volatile.Read(ref _cache);
		if (cache is not null && _time.GetUtcNow() - cache.FetchedAtUtc < Constants.Codex.Catalog.CacheMaxAge)
		{
			_log.Information("GPT catalog: {Count} models from the cache of {FetchedAt:u}", cache.Models.Count, cache.FetchedAtUtc);
			return;
		}

		await RefreshQuietlyAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async Task<bool> RefreshAsync(CancellationToken cancellationToken = default)
	{
		await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			var source = await _profiles.ResolveCatalogProfileAsync(cancellationToken).ConfigureAwait(false);
			if (source is null)
			{
				_log.Information("GPT catalog: no signed-in ChatGPT profile; the list stays as it is ({Count} models)", Models.Count);
				return false;
			}

			IReadOnlyList<CodexModel> listed;
			try
			{
				listed = await _pool.GetHost(source).ListModelsAsync(includeHidden: false, cancellationToken).ConfigureAwait(false);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				_log.Warning(ex, "GPT catalog: model/list on profile {Profile} failed; the cached list ({Count} models) is kept", source.ProfileId, Models.Count);
				return false;
			}

			var models = Order(listed.Where(m => !m.Hidden).Select(ToCatalogModel)).ToList();
			if (models.Count == 0)
			{
				// An empty answer is never stored: it would make every GPT id "not in the catalog" and move every Codex session to Claude.
				_log.Warning("GPT catalog: model/list on profile {Profile} returned no models; the cached list ({Count} models) is kept", source.ProfileId, Models.Count);
				return false;
			}

			var cache  = new CodexModelCacheModel { FetchedAtUtc = _time.GetUtcNow(), SourceProfileId = source.ProfileId, Models = models };
			Volatile.Write(ref _cache, cache);
			SaveCache(cache);
			_log.Information("GPT catalog: {Count} models from profile {Profile}", models.Count, source.ProfileId);
			Updated?.Invoke(this, EventArgs.Empty);
			return true;
		}
		finally
		{
			_refreshGate.Release();
		}
	}

	/// <summary>A sign-in refreshes the list (FR.33.2).</summary>
	private void OnSignedIn(string profileId) => _ = RefreshQuietlyAsync();

	private async Task RefreshQuietlyAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			await RefreshAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "GPT catalog refresh failed; the cached list is kept");
		}
	}

	/// <summary>The default model first, then the rest in Codex's order (FR.33.2).</summary>
	private IEnumerable<CodexCatalogModel> Order(IEnumerable<CodexCatalogModel> models)
	{
		var list = models.ToList();
		return list.Where(m => m.IsDefault).Concat(list.Where(m => !m.IsDefault));
	}

	private CodexCatalogModel ToCatalogModel(CodexModel model) => new(
		model.Id,
		string.IsNullOrWhiteSpace(model.DisplayName) ? model.Id : model.DisplayName,
		model.IsDefault,
		(model.SupportedReasoningEfforts ?? [])
			.Where(e => !string.Equals(e.ReasoningEffort, Constants.Codex.Catalog.UltraEffort, StringComparison.OrdinalIgnoreCase))
			.Select(e => new ModelEffortOptionModel(e.ReasoningEffort, e.Description))
			.ToList(),
		string.Equals(model.DefaultReasoningEffort, Constants.Codex.Catalog.UltraEffort, StringComparison.OrdinalIgnoreCase) ? null : model.DefaultReasoningEffort,
		model.InputModalities is { Count: > 0 } modalities ? modalities : Constants.Codex.Catalog.DefaultInputModalities,
		model.Upgrade);

	private void LoadCache()
	{
		try
		{
			if (!File.Exists(_cachePath))
				return;

			var cache = JsonSerializer.Deserialize<CodexModelCacheModel>(File.ReadAllText(_cachePath), Constants.Codex.SerializerOptions);
			if (cache is not null)
				Volatile.Write(ref _cache, cache);
		}
		catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
		{
			_log.Warning(ex, "GPT catalog cache {Path} could not be read; it is fetched again", _cachePath);
		}
	}

	private void SaveCache(CodexModelCacheModel cache)
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
			var temp = _cachePath + ".tmp";
			File.WriteAllText(temp, JsonSerializer.Serialize(cache, Constants.Codex.SerializerOptions), new UTF8Encoding(false));
			File.Move(temp, _cachePath, overwrite: true);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			_log.Warning(ex, "GPT catalog cache {Path} could not be written", _cachePath);
		}
	}
}
