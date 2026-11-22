using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TessynDesktop.Codex;
using TessynDesktop.Models;
using TessynDesktop.Services;
using TessynDesktop.Tests.Codex.Fakes;
using TessynDesktop.Tests.Services.Turns;

namespace TessynDesktop.Tests.Codex.Engine;

/// <summary>
/// The phase 3 Codex services over the in-process fake (FR.33.19): ChatGPT profiles, the GPT catalog with its cache, plan usage, the merged
/// catalog, the model-to-engine selector, the tier resolver and the profile resolver, on a temp data root with its own <c>codex-profiles</c>
/// folder and a fake Codex Default home.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class CodexServicesFixture : IAsyncDisposable
{
	public CodexServicesFixture(AppSettingsService? settings = null, InMemorySessionFileService? files = null)
	{
		Root = Path.Combine(Path.GetTempPath(), $"codex-p3-{Guid.NewGuid():N}");
		Directory.CreateDirectory(Root);
		Settings = settings ?? new AppSettingsService(Root);
		Files    = files ?? new InMemorySessionFileService();

		Pool      = new CodexHostPool(Launcher, time: Clock, executablePath: CodexEngineFixture.Executable);
		Profiles  = new CodexProfileService(Pool, Settings, StoredRoot, DefaultHome);
		Catalog   = new CodexModelCatalog(Pool, Profiles, Clock, Root);
		Usage     = new CodexUsageService(Pool, Profiles, Clock);
		Models    = new SessionModelCatalog(Claude, Catalog);
		Selector  = new ModelEngineSelector(Models);
		Tiers     = new ModelTierResolver(Settings, Models);
		Resolver  = new CodexProfileResolver(Settings, Profiles, Files);
	}

	public string Root { get; }
	public string StoredRoot => Path.Combine(Root, "codex-profiles");
	public string DefaultHome => Path.Combine(Root, "default-home", ".codex");
	public AppSettingsService Settings { get; }
	public InMemorySessionFileService Files { get; }
	public FakeCodexProcessLauncher Launcher { get; } = new();
	public ManualTimerTimeProvider Clock { get; } = new();
	public CodexHostPool Pool { get; }
	public CodexProfileService Profiles { get; }
	public CodexModelCatalog Catalog { get; }
	public CodexUsageService Usage { get; }
	public FixedClaudeModels Claude { get; } = new();
	public SessionModelCatalog Models { get; }
	public ModelEngineSelector Selector { get; }
	public ModelTierResolver Tiers { get; }
	public CodexProfileResolver Resolver { get; }
	public Eventually Eventually { get; } = new();

	public string CachePath => Path.Combine(Root, Constants.Codex.Catalog.CacheFileName);

	/// <summary>A stored ChatGPT profile <paramref name="id"/> signed in as <paramref name="email"/>, with its folder.</summary>
	public void AddStoredProfile(string id, string email, string plan = "plus")
	{
		Settings.Settings.CodexProfiles.Add(new CodexProfileModel { ProfileId = id, Email = email, PlanType = plan });
		Directory.CreateDirectory(Path.Combine(StoredRoot, id));
	}

	/// <summary>Loads the fake's default GPT list (gpt-x-sol default, gpt-x-astra, gpt-x-luna) into the catalog through a refresh on <paramref name="profileId"/>.</summary>
	public async Task LoadCatalogAsync(string profileId = "codex_1")
	{
		if (!Settings.Settings.CodexProfiles.Any(p => p.ProfileId == profileId))
			AddStoredProfile(profileId, "u@example.com");
		Settings.Settings.CodexCatalogProfileId = profileId;
		await Catalog.RefreshAsync();
	}

	/// <summary>Writes a cache file as the catalog stores it.</summary>
	public void WriteCache(DateTimeOffset fetchedAt, params CodexCatalogModel[] models) =>
		File.WriteAllText(CachePath, JsonSerializer.Serialize(new CodexModelCacheModel { FetchedAtUtc = fetchedAt, SourceProfileId = "codex_1", Models = [.. models] },
			Constants.Codex.SerializerOptions));

	public static CodexCatalogModel Cached(string id, bool isDefault = false, params string[] efforts) =>
		new(id, id.ToUpperInvariant(), isDefault, [.. efforts.Select(e => new ModelEffortOptionModel(e, null))], efforts.FirstOrDefault(), ["text", "image"], null);

	/// <summary>The params of every request of <paramref name="method"/> any fake server got, in order.</summary>
	public JsonElement[] Requests(string method) => [.. Launcher.Servers.SelectMany(s => s.RequestsOf(method)).Select(m => m.Params)];

	/// <summary>The fake server launched for <paramref name="profileId"/>'s home (the latest one).</summary>
	public FakeCodexAppServer ServerOf(string? codexHome) => Launcher.Servers.Last(s => s.Spec.CodexHome == codexHome);

	public async ValueTask DisposeAsync()
	{
		await Pool.DisposeAsync();
		try
		{
			Directory.Delete(Root, recursive: true);
		}
		catch (IOException)
		{
		}
	}
}
