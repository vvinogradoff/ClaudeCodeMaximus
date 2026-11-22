using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Codex;
using TessynDesktop.Models;
using TessynDesktop.Services;
using TessynDesktop.Tests.Codex.Engine;
using TessynDesktop.Tests.Codex.Fakes;
using TessynDesktop.Tests.Services;

namespace TessynDesktop.Tests.Contract;

/// <summary>
/// tdcp-spec 10.A14 on the wire: the real phase 3 Codex services over the in-process fake app-server (<see cref="CodexServicesFixture"/>), the real
/// settings, profile administration, catalog, usage, tree and hello modules, a dispatcher over them and a real loopback <see cref="TdcpServer"/>
/// with its local event stream. Codex is "found" at <see cref="CodexExe"/> by a fake locator environment that answers <c>codex-cli 0.171.0</c>.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class CodexWireEnv : IAsyncDisposable
{
	public const string CodexExe = @"C:\Tools\Codex\codex.exe";

	public CodexWireEnv()
	{
		Codex = new CodexServicesFixture();
		LocatorEnvironment.Files.Add(CodexExe);
		LocatorEnvironment.VersionOutputs[CodexExe] = "codex-cli 0.171.0";
		Settings.Settings.SessionFilesRoot = Path.Combine(Codex.Root, "sessions");
		Settings.Settings.ClaudePath       = "claude-test";
		Settings.Settings.CodexPath        = CodexExe;

		ClaudeProfiles = new ScriptedProfileService(Path.Combine(Codex.Root, "profiles"));
		ClaudeProfiles.SetEmail(null, "system.account@example.com");
		var labels = new ProfileLabelService(ClaudeProfiles, Settings);

		Sections     = new SettingsSectionService(Settings, [new GeneralSettingsSection(new CodexSettingsService(Settings, new CodexExecutableLocator(LocatorEnvironment)))], Bus);
		ProfileAdmin = new ProfileAdminService(ClaudeProfiles, Settings, Bus);
		CodexAdmin   = new CodexProfileAdminService(Codex.Profiles, Settings, Bus);
		Registry     = new SessionRegistry(Settings, new FakeSessionFileService(), new FakeDirectoryLabelService(), new FakeClaudeSessionStatusService());
		States       = new SessionStateBuilder(Registry, TestEngines.Claude(new FakeStreamingManagerForTurnService()), new FakeAttentionService(), Activity,
			codexProfiles: Codex.Profiles);
		Usage = new RemoteUsageService(UsageFetcher, new StubDesktopUsage(), ClaudeProfiles, Settings, labels, Bus, Codex.Clock, engineSources: [Codex.Usage]);

		var reader  = new RemoteTreeReader(Settings);
		var runInfo = new DaemonRunInfo();
		Dispatcher = new TdcpDispatcher(
		[
			new ClientRpcModule(runInfo: runInfo, eventLog: new EventLog(Bus), settingsSections: Sections, profileAdmin: ProfileAdmin, engines: Engines,
				codexProfiles: Codex.Profiles),
			new SettingsRpcModule(Sections),
			new ProfileAdminRpcModule(ProfileAdmin, labels, Codex.Profiles, CodexAdmin, Settings),
			new CatalogRpcModule(Codex.Claude, Settings, labels, Codex.Models, Codex.Profiles, Engines),
			new UsageRpcModule(Usage),
			new TreeRemoteRpcModule(reader, Registry, States, Activity, new TreeVersionTracker(), new FakeDirectoryLabelService(), new InProcFakeIndexService(),
				new FakeSessionSearchService()),
		]);
		Host = new TdcpTestHost(Dispatcher, bus: Bus, runInfo: runInfo);
	}

	public CodexServicesFixture Codex { get; }
	public AppSettingsService Settings => Codex.Settings;
	public FakeCodexLocatorEnvironment LocatorEnvironment { get; } = new();
	public ScriptedProfileService ClaudeProfiles { get; }
	public RemoteEventBus Bus { get; } = new();
	public SettingsSectionService Sections { get; }
	public ProfileAdminService ProfileAdmin { get; }
	public CodexProfileAdminService CodexAdmin { get; }
	public SessionRegistry Registry { get; }
	public ActivityByNodeService Activity { get; } = new();
	public SessionStateBuilder States { get; }
	public StubUsageFetcher UsageFetcher { get; } = new();
	public RemoteUsageService Usage { get; }
	public FixedEngineAvailability Engines { get; } = new() { Available = [Constants.SessionEngines.Claude, Constants.SessionEngines.Codex] };
	public TdcpDispatcher Dispatcher { get; }
	public TdcpTestHost Host { get; }

	public string StoredHome(string profileId) => Path.Combine(Codex.StoredRoot, profileId);

	/// <summary>The fake app-server of <paramref name="profileId"/>'s stored home (the latest one launched).</summary>
	public FakeCodexAppServer ServerOf(string profileId) => Codex.ServerOf(StoredHome(profileId));

	/// <summary>Codex reports the end of a sign-in on the profile's host.</summary>
	public Task CompleteLoginAsync(string profileId, string loginId, bool success, string? error = null) =>
		ServerOf(profileId).NotifyAsync("account/login/completed", new JsonObject { ["loginId"] = loginId, ["success"] = success, ["error"] = error });

	/// <summary>A JSON-RPC call through the dispatcher on <paramref name="transport"/>; the whole response.</summary>
	public async Task<JsonNode> CallRawAsync(string method, string paramsJson = "{}", TdcpTransport transport = TdcpTransport.Loopback)
	{
		var request = $$"""{"jsonrpc":"2.0","id":1,"method":"{{method}}","params":{{paramsJson}}}""";
		return (await Dispatcher.HandleTextAsync(request, transport, CancellationToken.None))!;
	}

	/// <summary>Like <see cref="CallRawAsync"/> but the <c>result</c>, failing on an error.</summary>
	public async Task<JsonNode> CallAsync(string method, string paramsJson = "{}", TdcpTransport transport = TdcpTransport.Loopback)
	{
		var response = await CallRawAsync(method, paramsJson, transport);
		if (response["error"] != null)
			throw new InvalidOperationException($"{method} failed: {response["error"]}");
		return response["result"]!;
	}

	/// <summary>The next frame of <paramref name="method"/> on the stream.</summary>
	public static async Task<SseTestFrame> NextAsync(SseTestClient sse, string method)
	{
		while (true)
		{
			var frame = await sse.NextAsync(TimeSpan.FromSeconds(10));
			if (frame.Method == method)
				return frame;
		}
	}

	public async ValueTask DisposeAsync()
	{
		Host.Dispose();
		Sections.Dispose();
		Bus.Dispose();
		await Codex.DisposeAsync();
	}
}
