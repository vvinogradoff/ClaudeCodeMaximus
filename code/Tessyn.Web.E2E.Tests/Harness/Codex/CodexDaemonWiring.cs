using Microsoft.Extensions.DependencyInjection;
using TessynDesktop;
using TessynDesktop.Codex;
using TessynDesktop.Models;
using TessynDesktop.Services;

namespace Tessyn.Web.E2E.Tests.Harness.Codex;

/// <summary>
/// The Codex engine of the E2E daemon (FR.33, TC.CX.42), registered as <c>DaemonHost</c> does but over the in-process fake app-server: the
/// launcher starts a <see cref="FakeCodexAppServer"/> per ChatGPT profile, and Codex is "found" at <see cref="CodexExe"/> by a fake locator
/// environment that answers <c>codex-cli 0.171.0</c>. The stored ChatGPT profile <see cref="ProfileId"/> is signed in and is the catalog profile.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class CodexDaemonWiring
{
	public const string CodexExe = @"C:\Tools\Codex\codex.exe";
	public const string ProfileId = "codex_1";
	public const string ProfileEmail = "u@example.com"; // what the fake app-server's account/read reports
	public const string ProfileName = "ChatGPT · " + ProfileEmail;
	public const string GptModelId = "gpt-x-sol";
	public const string OtherGptModelId = "gpt-x-luna";

	/// <returns>The host pool. It is registered as an instance, so the container does not dispose it (it is only <see cref="IAsyncDisposable"/>,
	/// which a synchronous container dispose rejects); the harness disposes it.</returns>
	internal static CodexHostPool Register(IServiceCollection s, string root)
	{
		var locatorEnvironment = new FakeCodexLocatorEnvironment();
		locatorEnvironment.Files.Add(CodexExe);
		locatorEnvironment.VersionOutputs[CodexExe] = "codex-cli 0.171.0";
		var locator  = new CodexExecutableLocator(locatorEnvironment);
		var launcher = new FakeCodexProcessLauncher();

		s.AddSingleton(launcher);
		s.AddSingleton<IMcpOAuthService, NoMcpOAuthService>();
		s.AddSingleton<IMcpConfigMerger, McpConfigMerger>();
		var pool = new CodexHostPool(launcher, locator, executablePath: CodexExe);
		s.AddSingleton(pool);
		s.AddTransient<CodexThreadConfigBuilder>();
		s.AddSingleton<ICodexProfileService>(sp => new CodexProfileService(sp.GetRequiredService<CodexHostPool>(), sp.GetRequiredService<IAppSettingsService>(),
			Path.Combine(root, "codex-profiles"), Path.Combine(root, "home", ".codex")));
		s.AddTransient<ICodexProfileResolver, CodexProfileResolver>();
		s.AddTransient<ICodexProfileSelector, CodexProfileResolver>();
		s.AddSingleton<ICodexModelCatalog>(sp => new CodexModelCatalog(sp.GetRequiredService<CodexHostPool>(), sp.GetRequiredService<ICodexProfileService>(),
			cacheDirectory: root));
		s.AddTransient<ISessionModelCatalog, SessionModelCatalog>();
		s.AddTransient<ISessionEngineSelector, ModelEngineSelector>();
		s.AddSingleton<CodexUsageService>();
		s.AddSingleton<IEngineUsageSource>(sp => sp.GetRequiredService<CodexUsageService>());
		s.AddTransient<IModelTierResolver, ModelTierResolver>();
		s.AddTransient(sp => new CodexSettingsService(sp.GetRequiredService<IAppSettingsService>(), locator));
		s.AddTransient<ISessionEngineAvailability>(sp => new SessionEngineAvailability(sp.GetServices<ISessionEngine>(), sp.GetRequiredService<IAppSettingsService>(), locator));
		s.AddSingleton<ICodexProfileAdminService, CodexProfileAdminService>();
		s.AddSingleton<ISessionEngine, CodexSessionEngine>();
		return pool;
	}

	/// <summary>The signed-in ChatGPT profile, the GPT catalog loaded from it, and one GPT session in the beta directory.</summary>
	internal static async Task<string> SeedAsync(IServiceProvider provider, string root, DirectoryNodeModel beta)
	{
		var settings = provider.GetRequiredService<IAppSettingsService>();
		var registry = provider.GetRequiredService<ISessionRegistry>();
		lock (settings.SyncRoot)
		{
			settings.Settings.CodexProfiles.Add(new CodexProfileModel { ProfileId = ProfileId, Email = ProfileEmail, PlanType = "plus" });
			settings.Settings.CodexCatalogProfileId = ProfileId;
			settings.Settings.CodexPath             = CodexExe; // the fake locator environment has no install folders, only this file
		}

		Directory.CreateDirectory(Path.Combine(root, "codex-profiles", ProfileId));
		if (!await provider.GetRequiredService<ICodexModelCatalog>().RefreshAsync())
			throw new InvalidOperationException("The GPT catalog did not load from the fake app-server");

		var gpt = registry.AddSession(beta, SeedModelCodex.GptSessionName);
		lock (settings.SyncRoot)
		{
			gpt.EngineId  = TessynDesktop.Constants.SessionEngines.Codex;
			gpt.ModelId   = GptModelId;
			gpt.ProfileId = ProfileId;
		}

		settings.Save();
		return gpt.NodeId;
	}
}

/// <summary>The names of the Codex seed (TC.CX.42).</summary>
/// <remarks>Created by Claude</remarks>
public static class SeedModelCodex
{
	public const string GptSessionName = "Beta GPT session";
}
